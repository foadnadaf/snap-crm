namespace SnapCrm.Api.Domain;

/// <summary>Immutable audit trail of every consent change (GDPR proof of opt-in/out).</summary>
public class ConsentRecord
{
    public int Id { get; set; }
    public string SourceUserId { get; set; } = default!;
    public ChannelType Channel { get; set; }
    public ConsentStatus Status { get; set; }
    public string? Source { get; set; }   // e.g. "signup-checkbox", "unsubscribe-link", "import"
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A saved audience definition. Rules are evaluated against CrmCustomer at run time.</summary>
public class Segment
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    /// <summary>Built-in segment key (e.g. "new", "loyal", "dormant", "vip") or "custom".</summary>
    public string RuleKey { get; set; } = "custom";
    /// <summary>Optional JSON parameters for the rule (e.g. { "dormantDays": 30, "plz": "1190" }).</summary>
    public string? RuleParamsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An email campaign. Flows through the semi-auto approval pipeline before sending.</summary>
public class Campaign
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public ChannelType Channel { get; set; } = ChannelType.Email;

    public int? SegmentId { get; set; }
    public Segment? Segment { get; set; }

    public string Subject { get; set; } = default!;
    public string HtmlBody { get; set; } = default!;

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public CreatedBy CreatedBy { get; set; } = CreatedBy.Human;

    /// <summary>Routine campaigns from a pre-approved recurring plan can skip re-approval.</summary>
    public bool IsRoutinePreApproved { get; set; }

    /// <summary>
    /// A double-opt-in "confirm your subscription" campaign. It targets customers who are
    /// NOT yet opted-in (so it bypasses the normal consent gate) and its body carries a
    /// {{confirm_url}} that opts the recipient IN when clicked.
    /// </summary>
    public bool IsRepermission { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    // Aggregates (filled as events arrive).
    public int RecipientCount { get; set; }
    public int SentCount { get; set; }
    public int OpenCount { get; set; }
    public int ClickCount { get; set; }

    public List<CampaignRecipient> Recipients { get; set; } = new();
}

public class CampaignRecipient
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = default!;

    public string SourceUserId { get; set; } = default!;
    public string Email { get; set; } = default!;
    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
    public string? SuppressReason { get; set; }
    public string? ProviderMessageId { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }
}

/// <summary>The semi-auto gate: an item a human must approve before anything is sent.</summary>
public class ApprovalItem
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = default!;

    public string Summary { get; set; } = default!;  // "Send 20% code to 320 dormant customers in 1190"
    public CreatedBy CreatedBy { get; set; }
    public ApprovalDecision Decision { get; set; } = ApprovalDecision.Pending;
    public string? DecidedByUser { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Raw delivery/engagement events from the ESP webhook (open/click/bounce/unsub).</summary>
public class EmailEvent
{
    public long Id { get; set; }
    public string Type { get; set; } = default!;   // delivered, opened, clicked, hard_bounce, unsubscribed...
    public string? Email { get; set; }
    public string? ProviderMessageId { get; set; }
    public int? CampaignId { get; set; }
    public string? RawJson { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
