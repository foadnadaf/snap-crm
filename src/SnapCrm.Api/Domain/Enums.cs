namespace SnapCrm.Api.Domain;

/// <summary>How a customer entered a marketing consent state (GDPR).</summary>
public enum ConsentStatus
{
    Unknown = 0,   // never asked / imported without proof -> NOT mailable for marketing
    OptedIn = 1,   // explicit opt-in on record -> mailable
    OptedOut = 2   // unsubscribed / objected -> never mail
}

public enum ChannelType
{
    Email = 0,
    Push = 1,
    Social = 2,
    Ads = 3
}

/// <summary>Lifecycle of a campaign in the semi-auto pipeline.</summary>
public enum CampaignStatus
{
    Draft = 0,          // being prepared (by human or agent Planner)
    PendingApproval = 1,// waiting in the approval queue
    Approved = 2,       // approved, will run at ScheduledAt
    Scheduled = 3,      // handed to the scheduler
    Sending = 4,
    Sent = 5,
    Rejected = 6,
    Canceled = 7,
    Failed = 8
}

public enum RecipientStatus
{
    Pending = 0,
    Suppressed = 1,  // skipped (no consent / unsubscribed / hard-bounced)
    Sent = 2,
    Delivered = 3,
    Opened = 4,
    Clicked = 5,
    Bounced = 6,
    Failed = 7
}

/// <summary>Who created the item — used to enforce the semi-auto approval rule.</summary>
public enum CreatedBy
{
    Human = 0,
    Agent = 1
}

public enum ApprovalDecision
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
