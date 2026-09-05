using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnapCrm.Api.Data;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Consent;

namespace SnapCrm.Api.Controllers;

/// <summary>
/// Receives delivery/engagement events from Brevo (opened, clicked, hard_bounce,
/// unsubscribed...) and updates recipients, aggregates, and consent/suppression.
/// Secured by a shared secret in the query string configured in the Brevo dashboard.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController(SnapCrmDbContext db, ConsentService consent, IConfiguration config,
    ILogger<WebhooksController> log) : ControllerBase
{
    [HttpPost("brevo")]
    public async Task<IActionResult> Brevo([FromQuery] string secret, [FromBody] JsonElement payload, CancellationToken ct)
    {
        var expected = config["Email:Brevo:WebhookSecret"];
        if (string.IsNullOrEmpty(expected) || secret != expected) return Unauthorized();

        var type = GetStr(payload, "event") ?? GetStr(payload, "type") ?? "";
        var email = GetStr(payload, "email");
        var messageId = GetStr(payload, "message-id") ?? GetStr(payload, "messageId");

        db.EmailEvents.Add(new EmailEvent
        {
            Type = type, Email = email, ProviderMessageId = messageId, RawJson = payload.ToString()
        });

        var recipient = messageId != null
            ? await db.CampaignRecipients.FirstOrDefaultAsync(r => r.ProviderMessageId == messageId, ct)
            : null;

        switch (type)
        {
            case "delivered": if (recipient != null) recipient.Status = RecipientStatus.Delivered; break;
            case "opened" or "unique_opened":
                if (recipient != null && recipient.OpenedAt == null)
                {
                    recipient.OpenedAt = DateTime.UtcNow; recipient.Status = RecipientStatus.Opened;
                    await Bump(recipient.CampaignId, open: true, ct);
                }
                break;
            case "click":
                if (recipient != null && recipient.ClickedAt == null)
                {
                    recipient.ClickedAt = DateTime.UtcNow; recipient.Status = RecipientStatus.Clicked;
                    await Bump(recipient.CampaignId, open: false, ct);
                }
                break;
            case "hard_bounce" or "blocked":
                if (recipient != null) recipient.Status = RecipientStatus.Bounced;
                if (email != null)
                {
                    var c = await db.Customers.FirstOrDefaultAsync(x => x.Email == email, ct);
                    if (c != null) c.EmailHardBounced = true;
                }
                break;
            case "unsubscribed" or "spam":
                if (email != null) await consent.OptOutByEmailAsync(email, $"esp-{type}", ct);
                break;
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private async Task Bump(int campaignId, bool open, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c == null) return;
        if (open) c.OpenCount++; else c.ClickCount++;
    }

    private static string? GetStr(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
