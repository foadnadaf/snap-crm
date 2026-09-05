using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Services.Approvals;

/// <summary>
/// The semi-auto gate. New campaigns land in the approval queue; only a human decision
/// (or a pre-approved routine plan) moves them to Approved. Nothing sends before that.
/// </summary>
public class ApprovalService(SnapCrmDbContext db, IConfiguration config, ILogger<ApprovalService> log)
{
    private bool RequireApproval => config.GetValue("Crm:RequireApprovalForNewCampaigns", true);

    /// <summary>
    /// Route a draft campaign. Routine + pre-approved recurring campaigns auto-approve;
    /// everything else waits for a human. Returns true if it now needs human approval.
    /// </summary>
    public async Task<bool> SubmitAsync(int campaignId, string summary, CancellationToken ct = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        var autoOk = campaign.IsRoutinePreApproved || !RequireApproval;
        if (autoOk)
        {
            campaign.Status = CampaignStatus.Approved;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Campaign {Id} auto-approved (routine/pre-approved).", campaignId);
            return false;
        }

        campaign.Status = CampaignStatus.PendingApproval;
        db.ApprovalItems.Add(new ApprovalItem
        {
            CampaignId = campaignId,
            Summary = summary,
            CreatedBy = campaign.CreatedBy,
            Decision = ApprovalDecision.Pending
        });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Campaign {Id} queued for human approval.", campaignId);
        return true;
    }

    public async Task ApproveAsync(int approvalId, string byUser, DateTime? scheduledAt, CancellationToken ct = default)
    {
        var item = await db.ApprovalItems.Include(a => a.Campaign)
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct)
            ?? throw new InvalidOperationException("Approval item not found.");

        item.Decision = ApprovalDecision.Approved;
        item.DecidedByUser = byUser;
        item.DecidedAt = DateTime.UtcNow;

        item.Campaign.Status = CampaignStatus.Approved;
        item.Campaign.ScheduledAt = scheduledAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(int approvalId, string byUser, string? note, CancellationToken ct = default)
    {
        var item = await db.ApprovalItems.Include(a => a.Campaign)
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct)
            ?? throw new InvalidOperationException("Approval item not found.");

        item.Decision = ApprovalDecision.Rejected;
        item.DecidedByUser = byUser;
        item.Note = note;
        item.DecidedAt = DateTime.UtcNow;
        item.Campaign.Status = CampaignStatus.Rejected;
        await db.SaveChangesAsync(ct);
    }
}
