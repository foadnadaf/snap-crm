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
}
