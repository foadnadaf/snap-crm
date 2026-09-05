using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SnapCrm.Api.Services.Email;

/// <summary>
/// Sends transactional/marketing email via Brevo (https://api.brevo.com/v3/smtp/email).
/// The API key is read from config/secret and never logged.
/// </summary>
public class BrevoEmailProvider : IEmailProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<BrevoEmailProvider> _log;
    private readonly string _fromName;
    private readonly string _fromEmail;
    private readonly string? _replyTo;

    public BrevoEmailProvider(HttpClient http, IConfiguration config, ILogger<BrevoEmailProvider> log)
    {
        _http = http;
        _log = log;
        var apiKey = config["Email:Brevo:ApiKey"] ?? "";
        var baseUrl = config["Email:Brevo:BaseUrl"] ?? "https://api.brevo.com/v3";
        _fromName = config["Email:FromName"] ?? "SnapFood";
        _fromEmail = config["Email:FromEmail"] ?? "hallo@snap-food.eu";
        _replyTo = config["Email:ReplyTo"];

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("api-key");
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("api-key", apiKey);
    }

    public async Task<EmailSendResult> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        // A List-Unsubscribe header (one-click) is required for good deliverability & law.
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(email.UnsubscribeUrl))
            headers["List-Unsubscribe"] = $"<{email.UnsubscribeUrl}>";

        var payload = new BrevoRequest
        {
            Sender = new BrevoContact { Name = _fromName, Email = _fromEmail },
            ReplyTo = string.IsNullOrWhiteSpace(_replyTo) ? null : new BrevoContact { Email = _replyTo! },
            To = new[] { new BrevoContact { Name = email.ToName, Email = email.ToEmail } },
            Subject = email.Subject,
            HtmlContent = email.HtmlBody,
            Headers = headers.Count > 0 ? headers : null,
            Tags = email.Tags?.Values.ToArray()
        };

        try
        {
            using var resp = await _http.PostAsJsonAsync("smtp/email", payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Brevo send failed {Status}: {Body}", (int)resp.StatusCode, body);
                return new EmailSendResult(false, null, $"HTTP {(int)resp.StatusCode}");
            }
            var parsed = await resp.Content.ReadFromJsonAsync<BrevoResponse>(cancellationToken: ct);
            return new EmailSendResult(true, parsed?.MessageId, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Brevo send threw.");
            return new EmailSendResult(false, null, ex.Message);
        }
    }

    // --- Brevo DTOs ---
    private class BrevoRequest
    {
        [JsonPropertyName("sender")] public BrevoContact Sender { get; set; } = default!;
        [JsonPropertyName("replyTo")] public BrevoContact? ReplyTo { get; set; }
        [JsonPropertyName("to")] public BrevoContact[] To { get; set; } = default!;
        [JsonPropertyName("subject")] public string Subject { get; set; } = default!;
        [JsonPropertyName("htmlContent")] public string HtmlContent { get; set; } = default!;
        [JsonPropertyName("headers")] public Dictionary<string, string>? Headers { get; set; }
        [JsonPropertyName("tags")] public string[]? Tags { get; set; }
    }
    private class BrevoContact
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; } = default!;
    }
    private class BrevoResponse
    {
        [JsonPropertyName("messageId")] public string? MessageId { get; set; }
    }
}
