using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SnapCrm.Api.Services.Sync;

/// <summary>
/// Reads customers + order aggregates from the production food DB using a READ-ONLY
/// connection. Only SELECT statements are issued here — no INSERT/UPDATE/DELETE ever.
/// The connection string should point at a replica or a db_datareader-only login.
///
/// NOTE: column/table names below reflect the food-server schema (Users, Orders). If the
/// real schema differs, adjust ONLY the SELECT text — the contract (SourceCustomer) stays.
/// </summary>
public class FoodDbSyncSource(IConfiguration config, ILogger<FoodDbSyncSource> logger) : ICustomerSyncSource
{
    private readonly string _cs = config.GetConnectionString("FoodDbReadOnly")
        ?? throw new InvalidOperationException("FoodDbReadOnly connection string is required.");

    public async Task<IReadOnlyList<SourceCustomer>> GetCustomersAsync(DateTime? changedSince, CancellationToken ct)
    {
        // Read-only aggregate. LEFT JOIN so customers with zero orders are still synced.
        // `changedSince` limits the scan to recently-active users for incremental syncs.
        // Matches the real food DB schema: Users has no PLZ/City (address lives in a
        // separate table), phone is MobileNumber, and Orders.CustomerId links to Users.Id.
        const string sql = @"
SELECT
    CONVERT(varchar(50), u.Id)               AS SourceUserId,
    u.Email                                  AS Email,
    u.MobileNumber                           AS Phone,
    u.FirstName                              AS FirstName,
    CAST(NULL AS nvarchar(20))               AS Plz,
    CAST(NULL AS nvarchar(100))              AS City,
    u.DateTime                               AS RegisteredAt,
    COALESCE(o.OrderCount, 0)                AS OrderCount,
    COALESCE(o.TotalSpent, 0)                AS TotalSpent,
    o.FirstOrderAt                           AS FirstOrderAt,
    o.LastOrderAt                            AS LastOrderAt
FROM Users u
OUTER APPLY (
    SELECT
        COUNT(*)         AS OrderCount,
        SUM(od.Total)    AS TotalSpent,
        MIN(od.DateTime) AS FirstOrderAt,
        MAX(od.DateTime) AS LastOrderAt
    FROM Orders od
    WHERE od.CustomerId = u.Id
      AND od.IsDeleted = 0
) o
WHERE (u.IsDeleted = 0)
  AND (@changedSince IS NULL OR u.UpdateDateTime >= @changedSince OR o.LastOrderAt >= @changedSince);";

        await using var conn = new SqlConnection(_cs);
        // ApplicationIntent=ReadOnly is respected by AlwaysOn replicas; harmless otherwise.
        var cmd = new CommandDefinition(sql, new { changedSince }, commandTimeout: 120, cancellationToken: ct);

        try
        {
            var rows = (await conn.QueryAsync<SourceCustomer>(cmd)).AsList();
            logger.LogInformation("Sync source returned {Count} customers (changedSince={Since}).", rows.Count, changedSince);
            return rows;
        }
        catch (SqlException ex)
        {
            // Fail safe: never let a sync error touch anything else. Log and return empty.
            logger.LogError(ex, "Read-only sync query failed. No data pulled this run.");
            return Array.Empty<SourceCustomer>();
        }
    }
}
