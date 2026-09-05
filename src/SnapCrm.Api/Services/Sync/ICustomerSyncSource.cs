namespace SnapCrm.Api.Services.Sync;

/// <summary>A read-only snapshot of one customer from the production food DB.</summary>
public record SourceCustomer(
    string SourceUserId,
    string? Email,
    string? Phone,
    string? FirstName,
    string? Plz,
    string? City,
    DateTime? RegisteredAt,
    int OrderCount,
    decimal TotalSpent,
    DateTime? FirstOrderAt,
    DateTime? LastOrderAt);

/// <summary>
/// Reads customers + order aggregates from production. Implementations MUST issue
/// read-only (SELECT) queries against a replica or a db_datareader-only login.
/// </summary>
public interface ICustomerSyncSource
{
    Task<IReadOnlyList<SourceCustomer>> GetCustomersAsync(DateTime? changedSince, CancellationToken ct);
}
