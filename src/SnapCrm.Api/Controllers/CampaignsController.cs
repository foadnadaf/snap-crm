using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Campaigns;

namespace SnapCrm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CampaignsController(SnapCrmDbContext db, CampaignService campaigns,
    SnapCrm.Api.Services.Approvals.ApprovalService approvals) : ControllerBase
{
    public record CreateCampaignDto(string Name, int SegmentId, string Subject, string HtmlBody, bool RoutinePreApproved);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.Campaigns.OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Name, c.Status, c.CreatedBy, c.RecipientCount, c.SentCount, c.OpenCount, c.ClickCount, c.ScheduledAt, c.CreatedAt })
            .ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCampaignDto dto, CancellationToken ct)
    {
        var c = new Campaign
        {
            Name = dto.Name,
            SegmentId = dto.SegmentId,
            Subject = dto.Subject,
            HtmlBody = dto.HtmlBody,
            CreatedBy = CreatedBy.Human,
            IsRoutinePreApproved = dto.RoutinePreApproved,
            Status = CampaignStatus.Draft
        };
        db.Campaigns.Add(c);
        await db.SaveChangesAsync(ct);
        return Ok(new { c.Id });
    }

    /// <summary>Freeze the audience so counts are stable before approval.</summary>
    [HttpPost("{id:int}/build-recipients")]
    public async Task<IActionResult> Build(int id, CancellationToken ct) =>
        Ok(new { recipients = await campaigns.BuildRecipientsAsync(id, ct) });

    /// <summary>Submit for approval (semi-auto gate). Returns whether it needs a human.</summary>
    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> Submit(int id, [FromQuery] string? summary, CancellationToken ct)
    {
        var needsHuman = await approvals.SubmitAsync(id, summary ?? $"Campaign #{id}", ct);
        return Ok(new { needsHumanApproval = needsHuman });
    }

    /// <summary>Manually push a send batch for an approved campaign (also runs on a schedule).</summary>
    [HttpPost("{id:int}/send-batch")]
    public async Task<IActionResult> Send(int id, CancellationToken ct) =>
        Ok(new { sent = await campaigns.SendApprovedAsync(id, ct) });

    /// <summary>
    /// Create the double-opt-in "confirm your subscription" campaign, freeze its audience
    /// (everyone undecided), and drop it into the approval queue. Nothing sends until you
    /// approve it AND Crm:SendingEnabled is true.
    /// </summary>
    [HttpPost("repermission")]
    public async Task<IActionResult> CreateRepermission(CancellationToken ct)
    {
        var c = new Campaign
        {
            Name = "Double opt-in – Anmeldung bestätigen",
            Subject = "Möchtest du SnapFood-Angebote erhalten? 🍔",
            HtmlBody = RepermissionBody,
            IsRepermission = true,
            CreatedBy = CreatedBy.Human,
            Status = CampaignStatus.Draft
        };
        db.Campaigns.Add(c);
        await db.SaveChangesAsync(ct);

        var recipients = await campaigns.BuildRecipientsAsync(c.Id, ct);
        var needsHuman = await approvals.SubmitAsync(c.Id, $"[Re-permission] Anmelde-Bestätigung an {recipients} Kontakte.", ct);
        return Ok(new { c.Id, recipients, needsHumanApproval = needsHuman });
    }

    /// <summary>Send the re-permission email to ONE address (test before the mass send).</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromQuery] string email, CancellationToken ct) =>
        Ok(await campaigns.SendOneAsync(email, "TEST – SnapFood: Anmeldung bestätigen", RepermissionBody, ct));

    private const string RepermissionBody = @"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:auto"">
  <h2 style=""color:#e11d2a"">Möchtest du SnapFood-Angebote erhalten?</h2>
  <p>Hallo! Wir möchten dir gerne exklusive Angebote und Neuigkeiten von SnapFood per E-Mail schicken.</p>
  <p>Bitte bestätige mit einem Klick, dass du diese E-Mails erhalten möchtest:</p>
  <p><a href=""{{confirm_url}}"" style=""background:#e11d2a;color:#fff;padding:12px 22px;border-radius:8px;text-decoration:none;display:inline-block"">Ja, ich bin dabei</a></p>
  <hr style=""border:none;border-top:1px solid #eee;margin:24px 0"">
  <p style=""font-size:12px;color:#888"">Wenn du keine E-Mails möchtest, ignoriere diese Nachricht einfach – oder <a href=""{{unsubscribe_url}}"">hier dauerhaft abmelden</a>.</p>
</div>";
}
