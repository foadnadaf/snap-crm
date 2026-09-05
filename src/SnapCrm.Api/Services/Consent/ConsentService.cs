using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Services.Consent;

/// <summary>
/// The single source of truth for marketing consent. Every change writes an immutable
/// ConsentRecord (GDPR proof) AND updates the customer's current consent flag.
/// </summary>
public class ConsentService(SnapCrmDbContext db, ILogger<ConsentService> log)
{
    public async Task SetAsync(string sourceUserId, ChannelType channel, ConsentStatus status,
        string source, string? ip = null, CancellationToken ct = default)
    {
        db.ConsentRecords.Add(new ConsentRecord
        {
            SourceUserId = sourceUserId,
            Channel = channel,
            Status = status,
            Source = source,
            Ip = ip
        });

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.SourceUserId == sourceUserId, ct);
        if (customer != null)
        {
            if (channel == ChannelType.Email) customer.EmailConsent = status;
            else if (channel == ChannelType.Push) customer.PushConsent = status;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Consent {Channel}={Status} for {User} via {Source}.", channel, status, sourceUserId, source);
    }

    public Task OptOutByEmailAsync(string email, string source, CancellationToken ct = default)
        => SetByEmailAsync(email, ChannelType.Email, ConsentStatus.OptedOut, source, ct);

    public async Task SetByEmailAsync(string email, ChannelType channel, ConsentStatus status,
        string source, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer == null) return;
        await SetAsync(customer.SourceUserId, channel, status, source, ct: ct);
    }
}
