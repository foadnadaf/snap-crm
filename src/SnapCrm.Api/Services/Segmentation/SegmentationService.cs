using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;

namespace SnapCrm.Api.Services.Segmentation;

/// <summary>
/// Turns a Segment definition into the set of customers it matches. Every result is
/// additionally filtered by IsEmailMarketable for email channels, so a segment can never
/// accidentally include a non-consented / unsubscribed / bounced customer.
/// </summary>
public class SegmentationService(SnapCrmDbContext db)
{
    /// <summary>Built-in rule keys the UI/agent can pick from.</summary>
    public static readonly string[] BuiltInKeys =
        { "all", "new", "loyal", "dormant", "vip", "plz" };

    public IQueryable<CrmCustomer> Resolve(Segment segment, bool emailMarketableOnly = true)
    {
        var q = db.Customers.AsQueryable();
        var p = Params(segment.RuleParamsJson);

        q = segment.RuleKey switch
        {
            "new"     => q.Where(c => c.OrderCount == 0),
            "loyal"   => q.Where(c => c.OrderCount >= GetInt(p, "minOrders", 5)),
            "dormant" => q.Where(c => c.LastOrderAt != null &&
                                      c.DaysSinceLastOrder >= GetInt(p, "dormantDays", 30)),
            "vip"     => q.Where(c => c.TotalSpent >= GetDecimal(p, "minSpent", 200m)),
            "plz"     => q.Where(c => c.Plz == GetString(p, "plz")),
            _          => q // "all" / custom
        };

        if (emailMarketableOnly)
            q = q.Where(c => c.EmailConsent == ConsentStatus.OptedIn
                             && !c.EmailHardBounced
                             && c.Email != null && c.Email != "");

        return q;
    }

    public Task<int> CountAsync(Segment segment, CancellationToken ct = default)
        => Resolve(segment).CountAsync(ct);

    // --- helpers ---
    private static Dictionary<string, JsonElement> Params(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new(); }
        catch { return new(); }
    }
    private static int GetInt(Dictionary<string, JsonElement> p, string k, int d)
        => p.TryGetValue(k, out var v) && v.TryGetInt32(out var i) ? i : d;
    private static decimal GetDecimal(Dictionary<string, JsonElement> p, string k, decimal d)
        => p.TryGetValue(k, out var v) && v.TryGetDecimal(out var i) ? i : d;
    private static string GetString(Dictionary<string, JsonElement> p, string k)
        => p.TryGetValue(k, out var v) ? v.ToString() : "__none__";
}
