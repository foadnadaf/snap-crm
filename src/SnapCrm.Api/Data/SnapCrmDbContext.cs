using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Data;

/// <summary>
/// SnapCRM's OWN database context. Points at CrmDb only — a separate catalog from
/// production. Production data is never modified here (it arrives read-only via the sync).
/// </summary>
public class SnapCrmDbContext(DbContextOptions<SnapCrmDbContext> options) : DbContext(options)
{
    public DbSet<CrmCustomer> Customers => Set<CrmCustomer>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<ApprovalItem> ApprovalItems => Set<ApprovalItem>();
    public DbSet<EmailEvent> EmailEvents => Set<EmailEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CrmCustomer>(e =>
        {
            e.HasIndex(x => x.SourceUserId).IsUnique();
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.LastOrderAt);
            e.Property(x => x.TotalSpent).HasPrecision(18, 2);
        });

        b.Entity<ConsentRecord>(e =>
        {
            e.HasIndex(x => new { x.SourceUserId, x.Channel });
        });

        b.Entity<Campaign>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(300);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Segment).WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CampaignRecipient>(e =>
        {
            e.HasIndex(x => new { x.CampaignId, x.Status });
            e.HasIndex(x => x.ProviderMessageId);
            e.HasOne(x => x.Campaign).WithMany(c => c.Recipients).HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApprovalItem>(e =>
        {
            e.HasIndex(x => x.Decision);
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailEvent>(e =>
        {
            e.HasIndex(x => x.ProviderMessageId);
            e.HasIndex(x => x.ReceivedAt);
        });
    }
}
