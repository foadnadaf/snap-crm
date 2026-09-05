namespace SnapCrm.Api.Services.Email;

public record EmailSendResult(bool Success, string? ProviderMessageId, string? Error);

public record OutgoingEmail(
    string ToEmail,
    string? ToName,
    string Subject,
    string HtmlBody,
    string? UnsubscribeUrl,
    IDictionary<string, string>? Tags = null);

/// <summary>Abstraction over the email service provider (ESP). Brevo is the default impl.</summary>
public interface IEmailProvider
{
    Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default);
}
