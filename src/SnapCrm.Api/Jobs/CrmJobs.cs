using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Agent;
using SnapCrm.Api.Services.Campaigns;
using SnapCrm.Api.Services.Sync;

namespace SnapCrm.Api.Jobs;

/// <summary>Recurring background jobs. Each is safe to run repeatedly and independently.</summary>
public class CrmJobs(
    SyncService sync,
    PlannerService planner,
    CampaignService campaigns,
    SnapCrmDbContext db,
    ILogger<CrmJobs> log)
{
    /// <summary>Pull latest customers/orders from production (read-only) into the CRM DB.</summary>
    public Task SyncAsync() => sync.RunAsync();

    /// <summary>Let the semi-auto agent propose campaigns into the approval queue.</summary>
    public Task PlanAsync() => planner.ProposeAsync();

    /// <summary>Send small batches for every approved/scheduled campaign that is due.</summary>
    public async Task DispatchAsync()
    {
        var due = await db.Campaigns
            .Where(c => (c.Status == CampaignStatus.Approved || c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Sending)
                        && (c.ScheduledAt == null || c.ScheduledAt <= DateTime.UtcNow))
            .Select(c => c.Id)
            .ToListAsync();

        foreach (var id in due)
            await campaigns.SendApprovedAsync(id);

        if (due.Count > 0) log.LogInformation("Dispatcher processed {Count} campaign(s).", due.Count);
    }
}
