namespace SnapCrm.Api.Domain;

/// <summary>
/// A read-only projection of a production customer, enriched with CRM/marketing
/// fields. Synced one-way from the food DB; SnapCRM never writes back to production.
/// </summary>
public class CrmCustomer
{
    public int Id { get; set; }

    // --- Identity (mirrored from production, keyed by the source user id) ---
    public string SourceUserId { get; set; } = default!; // GUID string from Users table
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? Plz { get; set; }
    public string? City { get; set; }
    public DateTime? RegisteredAt { get; set; }

    // --- Behaviour (computed from order summaries) ---
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? FirstOrderAt { get; set; }
    public DateTime? LastOrderAt { get; set; }
    public int? DaysSinceLastOrder { get; set; }

    // --- Marketing / GDPR ---
    public ConsentStatus EmailConsent { get; set; } = ConsentStatus.Unknown;
    public ConsentStatus PushConsent { get; set; } = ConsentStatus.Unknown;
    public bool EmailHardBounced { get; set; }

    // --- Housekeeping ---
    public DateTime LastSyncedAt { get; set; }

    /// <summary>A customer may receive marketing email only with an explicit opt-in and no bounce.</summary>
    public bool IsEmailMarketable =>
        EmailConsent == ConsentStatus.OptedIn &&
        !EmailHardBounced &&
        !string.IsNullOrWhiteSpace(Email);
}
