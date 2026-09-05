using Microsoft.AspNetCore.Mvc;
using SnapCrm.Api.Services.Consent;

namespace SnapCrm.Api.Controllers;

/// <summary>
/// Public one-click unsubscribe target (linked from every marketing email). Required by
/// EU law. Validates a signed token so no one can unsubscribe another person.
/// </summary>
[ApiController]
[Route("[controller]")]
public class UnsubscribeController(UnsubscribeTokens tokens, ConsentService consent, ILogger<UnsubscribeController> log)
    : ControllerBase
{
    [HttpGet("/unsubscribe")]
    public async Task<IActionResult> Get([FromQuery] string t, CancellationToken ct)
    {
        if (!tokens.TryValidate(t, out var email))
            return Content(Page("Ungültiger Link."), "text/html");

        await consent.OptOutByEmailAsync(email, "unsubscribe-link", ct);
        log.LogInformation("Unsubscribe processed for {Email}.", email);
        return Content(Page("Du wurdest erfolgreich abgemeldet. Du erhältst keine Marketing-E-Mails mehr."), "text/html");
    }

    private static string Page(string msg) => $@"<!doctype html><html lang=""de""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>SnapFood</title></head>
<body style=""font-family:Arial,sans-serif;text-align:center;padding:60px 20px"">
<h2 style=""color:#e11d2a"">SnapFood</h2><p>{System.Net.WebUtility.HtmlEncode(msg)}</p></body></html>";
}
