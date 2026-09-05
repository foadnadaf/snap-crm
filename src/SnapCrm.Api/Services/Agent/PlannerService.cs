using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Approvals;
using SnapCrm.Api.Services.Segmentation;

namespace SnapCrm.Api.Services.Agent;

/// <summary>
/// The semi-auto "agent" — Phase 1 version. It looks at the data, drafts sensible
/// campaigns, and drops them into the approval queue. It NEVER sends anything itself;
/// a human approves (except routine pre-approved plans). Content generation via an LLM
/// can be plugged into BuildBody later; for now it uses safe built-in templates.
/// </summary>
public class PlannerService(
    SnapCrmDbContext db,
    SegmentationService segments,
    ApprovalService approvals,
    ILogger<PlannerService> log)
{
    /// <summary>Scan segments and propose campaigns worth running. Returns count proposed.</summary>
    public async Task<int> ProposeAsync(CancellationToken ct = default)
    {
        var proposed = 0;

        // Idea 1: win back dormant customers (30+ days), if the audience is meaningful.
        proposed += await ProposeForSegmentAsync(
            key: "dormant",
            name: "Reaktivierung – seit 30 Tagen inaktiv",
            subject: "Wir vermissen dich – 20% auf deine nächste Bestellung 🍔",
            minAudience: 25, ct: ct);

        // Idea 2: welcome the newly-registered who never ordered.
        proposed += await ProposeForSegmentAsync(
            key: "new",
            name: "Willkommen – noch keine Bestellung",
            subject: "Willkommen bei SnapFood – so bestellst du in 2 Minuten",
            minAudience: 10, ct: ct);

        log.LogInformation("Planner proposed {Count} campaign(s).", proposed);
        return proposed;
    }

    private async Task<int> ProposeForSegmentAsync(string key, string name, string subject, int minAudience, CancellationToken ct)
    {
        var segment = await db.Segments.FirstOrDefaultAsync(s => s.RuleKey == key && s.IsActive, ct);
        if (segment == null)
        {
            segment = new Segment { Name = name, RuleKey = key, IsActive = true };
            db.Segments.Add(segment);
            await db.SaveChangesAsync(ct);
        }

        var audience = await segments.CountAsync(segment, ct);
        if (audience < minAudience) return 0;

        // Avoid duplicate proposals: skip if an open campaign already targets this segment.
        var alreadyOpen = await db.Campaigns.AnyAsync(c =>
            c.SegmentId == segment.Id &&
            (c.Status == CampaignStatus.PendingApproval || c.Status == CampaignStatus.Draft), ct);
        if (alreadyOpen) return 0;

        var campaign = new Campaign
        {
            Name = name,
            Channel = ChannelType.Email,
            SegmentId = segment.Id,
            Subject = subject,
            HtmlBody = BuildBody(subject),
            CreatedBy = CreatedBy.Agent,
            Status = CampaignStatus.Draft,
            IsRoutinePreApproved = false // agent proposals always need human approval
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        await approvals.SubmitAsync(campaign.Id,
            $"[Agent] „{name}“ an {audience} Empfänger (Segment: {key}).", ct);
        return 1;
    }

    /// <summary>Placeholder templated body. Swap for an LLM-generated body later.</summary>
    private static string BuildBody(string subject) => $@"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:auto"">
  <h2 style=""color:#e11d2a"">{System.Net.WebUtility.HtmlEncode(subject)}</h2>
  <p>Hallo!</p>
  <p>Schön, dass du bei <b>SnapFood</b> bist. Entdecke deine Lieblingsrestaurants und
     bestelle in wenigen Minuten.</p>
  <p><a href=""https://snap-food.at"" style=""background:#e11d2a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block"">Jetzt bestellen</a></p>
  <hr style=""border:none;border-top:1px solid #eee;margin:24px 0"">
  <p style=""font-size:12px;color:#888"">
    Du erhältst diese E-Mail, weil du dem Erhalt von SnapFood-News zugestimmt hast.
    <a href=""{{{{unsubscribe_url}}}}"">Abmelden</a>.
  </p>
</div>";
}
