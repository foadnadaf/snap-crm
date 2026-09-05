using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Services.Sync;

/// <summary>
/// One-way sync: production (read-only) -> CRM DB (write). Upserts customers and
/// recomputes behaviour fields. Marketing consent is NEVER inferred here — it is only
/// ever set through explicit ConsentRecord writes, so importing a customer does not make
/// them mailable.
/// </summary>
public class SyncService(
    ICustomerSyncSource source,
    SnapCrmDbContext db,
    ILogger<SyncService> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Incremental window: look back a bit past the last sync to catch stragglers.
        var lastSync = await db.Customers.MaxAsync(c => (DateTime?)c.LastSyncedAt, ct);
        var since = lastSync?.AddHours(-2);

        var rows = await source.GetCustomersAsync(since, ct);
        if (rows.Count == 0) return 0;

        var ids = rows.Select(r => r.SourceUserId).ToHashSet();
        var existing = await db.Customers
            .Where(c => ids.Contains(c.SourceUserId))
            .ToDictionaryAsync(c => c.SourceUserId, ct);

        var now = DateTime.UtcNow;
        var upserts = 0;

        foreach (var r in rows)
        {
            if (!existing.TryGetValue(r.SourceUserId, out var c))
            {
                c = new CrmCustomer { SourceUserId = r.SourceUserId };
                db.Customers.Add(c);
                existing[r.SourceUserId] = c;
            }

            // Identity + behaviour are safe to overwrite from source.
            c.Email = r.Email?.Trim();
            c.Phone = r.Phone;
            c.FirstName = r.FirstName;
            c.Plz = r.Plz;
            c.City = r.City;
            c.RegisteredAt = r.RegisteredAt;
            c.OrderCount = r.OrderCount;
            c.TotalSpent = r.TotalSpent;
            c.FirstOrderAt = r.FirstOrderAt;
            c.LastOrderAt = r.LastOrderAt;
            c.DaysSinceLastOrder = r.LastOrderAt.HasValue
                ? (int)(now - r.LastOrderAt.Value).TotalDays
                : null;
            c.LastSyncedAt = now;
            // NOTE: EmailConsent / PushConsent are intentionally left untouched.
            upserts++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sync upserted {Count} customers.", upserts);
        return upserts;
    }
}
