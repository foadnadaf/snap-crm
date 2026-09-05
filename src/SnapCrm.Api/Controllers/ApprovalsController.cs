using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Approvals;

namespace SnapCrm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApprovalsController(SnapCrmDbContext db, ApprovalService approvals) : ControllerBase
{
    public record DecisionDto(string ByUser, DateTime? ScheduledAt, string? Note);

    /// <summary>The human review queue — everything waiting for a yes/no.</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) =>
        Ok(await db.ApprovalItems
            .Where(a => a.Decision == ApprovalDecision.Pending)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id, a.CampaignId, a.Summary, a.CreatedBy, a.CreatedAt,
                Campaign = new { a.Campaign.Name, a.Campaign.Subject, a.Campaign.RecipientCount }
            })
            .ToListAsync(ct));

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] DecisionDto dto, CancellationToken ct)
    {
        await approvals.ApproveAsync(id, dto.ByUser, dto.ScheduledAt, ct);
        return Ok();
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] DecisionDto dto, CancellationToken ct)
    {
        await approvals.RejectAsync(id, dto.ByUser, dto.Note, ct);
        return Ok();
    }
}
