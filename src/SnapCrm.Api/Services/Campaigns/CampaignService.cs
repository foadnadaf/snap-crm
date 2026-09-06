using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Consent;
using SnapCrm.Api.Services.Email;
using SnapCrm.Api.Services.Segmentation;

namespace SnapCrm.Api.Services.Campaigns;

/// <summary>
/// Materialises recipients from a segment and sends an approved campaign. Every guardrail
/// lives here: master kill-switch, per-day cap, allowed hours, consent + suppression.
/// </summary>
public class CampaignService(
    SnapCrmDbContext db,
    SegmentationService segments,
    IEmailProvider email,
    UnsubscribeTokens unsubscribe,
    IConfiguration config,
    ILogger<CampaignService> log)
{
    private bool SendingEnabled => config.GetValue("Crm:SendingEnabled", false);
    private int MaxPerDay => config.GetValue("Crm:MaxEmailsPerDay", 2000);
    private int BatchSize => config.GetValue("Crm:MaxEmailsPerRunBatch", 200);
    private int HourFrom => config.GetValue("Crm:AllowedSendHoursLocal:From", 8);
    private int HourTo => config.GetValue("Crm:AllowedSendHoursLocal:To", 20);
    private string BaseUrl => config["Crm:PublicBaseUrl"] ?? "https://crm.snap-food.at";

    private record CustomerRef(string SourceUserId, string Email);

    /// <summary>Freeze the current segment membership into CampaignRecipient rows.</summary>
    public async Task<int> BuildRecipientsAsync(int campaignId, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.Include(c => c.Segment)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        // Clear any previous materialisation.
        await db.CampaignRecipients.Where(r => r.CampaignId == campaignId).ExecuteDeleteAsync(ct);

        List<CustomerRef> people;
        if (campaign.IsRepermission)
        {
            // Double-opt-in ask: everyone with an email who has NOT yet decided
            // (Unknown consent), never opted out, and not hard-bounced.
            people = await db.Customers
                .Where(c => c.Email != null && c.Email != ""
                            && c.EmailConsent == ConsentStatus.Unknown
                            && !c.EmailHardBounced)
                .Select(c => new CustomerRef(c.SourceUserId, c.Email!))
                .ToListAsync(ct);
        }
        else
        {
            if (campaign.Segment == null) throw new InvalidOperationException("Campaign has no segment.");
            people = await segments.Resolve(campaign.Segment) // already email-marketable-only
                .Select(c => new CustomerRef(c.SourceUserId, c.Email!))
                .ToListAsync(ct);
        }

        foreach (var p in people)
        {
            db.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = campaignId,
                SourceUserId = p.SourceUserId,
                Email = p.Email!,
                Status = RecipientStatus.Pending
            });
        }
        campaign.RecipientCount = people.Count;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Campaign {Id} materialised {Count} recipients.", campaignId, people.Count);
        return people.Count;
    }

    /// <summary>Send pending recipients of an APPROVED campaign, respecting all guardrails.</summary>
    public async Task<int> SendApprovedAsync(int campaignId, CancellationToken ct = default)
    {
        if (!SendingEnabled)
        {
            log.LogWarning("Kill-switch is ON (Crm:SendingEnabled=false). Campaign {Id} not sent.", campaignId);
            return 0;
        }

        var nowLocal = DateTime.Now; // server local; keep server TZ = Europe/Vienna
        if (nowLocal.Hour < HourFrom || nowLocal.Hour >= HourTo)
        {
            log.LogInformation("Outside allowed send window ({From}-{To}). Deferring campaign {Id}.", HourFrom, HourTo, campaignId);
            return 0;
        }

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null || campaign.Status is not (CampaignStatus.Approved or CampaignStatus.Scheduled or CampaignStatus.Sending))
        {
            log.LogWarning("Campaign {Id} is not in a sendable state.", campaignId);
            return 0;
        }

        var sentToday = await db.CampaignRecipients
            .CountAsync(r => r.SentAt != null && r.SentAt >= DateTime.UtcNow.Date, ct);
        var remainingDailyBudget = Math.Max(0, MaxPerDay - sentToday);
        if (remainingDailyBudget == 0) { log.LogInformation("Daily email cap reached."); return 0; }

        campaign.Status = CampaignStatus.Sending;
        await db.SaveChangesAsync(ct);

        var take = Math.Min(BatchSize, remainingDailyBudget);
        var batch = await db.CampaignRecipients
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
            .Take(take).ToListAsync(ct);

        var sent = 0;
        foreach (var r in batch)
        {
            ct.ThrowIfCancellationRequested();

            // Re-check consent at send time (belt & suspenders). For a re-permission ask we
            // require only that the recipient has NOT opted out / bounced; for any normal
            // campaign we require an explicit opt-in.
            var okToSend = campaign.IsRepermission
                ? await db.Customers.AnyAsync(c => c.SourceUserId == r.SourceUserId &&
                      c.EmailConsent != ConsentStatus.OptedOut && !c.EmailHardBounced, ct)
                : await db.Customers.AnyAsync(c => c.SourceUserId == r.SourceUserId &&
                      c.EmailConsent == ConsentStatus.OptedIn && !c.EmailHardBounced, ct);
            if (!okToSend)
            {
                r.Status = RecipientStatus.Suppressed;
                r.SuppressReason = campaign.IsRepermission ? "opted-out-or-bounced" : "no-consent-at-send";
                continue;
            }

            var unsubUrl = $"{BaseUrl}/unsubscribe?t={unsubscribe.Create(r.Email)}";
            var body = campaign.HtmlBody
                .Replace("{{unsubscribe_url}}", unsubUrl)
                .Replace("{{confirm_url}}", $"{BaseUrl}/confirm?t={unsubscribe.Create(r.Email)}");

            var result = await email.SendAsync(new OutgoingEmail(
                r.Email, null, campaign.Subject, body, unsubUrl,
                new Dictionary<string, string> { ["campaign"] = campaignId.ToString() }), ct);

            if (result.Success)
            {
                r.Status = RecipientStatus.Sent;
                r.SentAt = DateTime.UtcNow;
                r.ProviderMessageId = result.ProviderMessageId;
                sent++;
            }
            else
            {
                r.Status = RecipientStatus.Failed;
                r.SuppressReason = result.Error;
            }
        }

        campaign.SentCount += sent;
        var anyPending = await db.CampaignRecipients
            .AnyAsync(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending, ct);
        if (!anyPending)
        {
            campaign.Status = CampaignStatus.Sent;
            campaign.SentAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Campaign {Id}: sent {Sent} this batch.", campaignId, sent);
        return sent;
    }
}
