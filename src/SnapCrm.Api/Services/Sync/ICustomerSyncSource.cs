namespace SnapCrm.Api.Services.Sync;

/// <summary>
/// A read-only snapshot of one customer from the production food DB. A mutable class
/// (not a positional record) so Dapper maps by column NAME and tolerates DB type
/// differences (e.g. a float Total column -> decimal here).
/// </summary>
public class SourceCustomer
{
    public string SourceUserId { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? Plz { get; set; }
    public string? City { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? FirstOrderAt { get; set; }
    public DateTime? LastOrderAt { get; set; }
}

/// <summary>
/// Reads customers + order aggregates from production. Implementations MUST issue
/// read-only (SELECT) queries against a replica or a db_datareader-only login.
/// </summary>
public interface ICustomerSyncSource
{
    Task<IReadOnlyList<SourceCustomer>> GetCustomersAsync(DateTime? changedSince, CancellationToken ct);
}
