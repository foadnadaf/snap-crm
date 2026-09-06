using Microsoft.AspNetCore.Mvc;
using SnapCrm.Api.Domain;
using SnapCrm.Api.Services.Consent;

namespace SnapCrm.Api.Controllers;

/// <summary>
/// Public double-opt-in confirmation target. Clicked from the re-permission email; a valid
/// signed token opts the recipient IN. Uses the same HMAC signing as unsubscribe so nobody
/// can opt someone else in.
/// </summary>
[ApiController]
public class ConfirmController(UnsubscribeTokens tokens, ConsentService consent, ILogger<ConfirmController> log)
    : ControllerBase
{
    [HttpGet("/confirm")]
    public async Task<IActionResult> Get([FromQuery] string t, CancellationToken ct)
    {
        if (!tokens.TryValidate(t, out var email))
            return Content(Page("Ungültiger oder abgelaufener Link."), "text/html");

        await consent.SetByEmailAsync(email, ChannelType.Email, ConsentStatus.OptedIn, "re-permission-confirm", ct);
        log.LogInformation("Opt-in confirmed for {Email}.", email);
        return Content(Page("Danke! Deine Anmeldung ist bestätigt. Du erhältst ab jetzt SnapFood-Angebote."), "text/html");
    }

    private static string Page(string msg) => $@"<!doctype html><html lang=""de""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>SnapFood</title></head>
<body style=""font-family:Arial,sans-serif;text-align:center;padding:60px 20px"">
<h2 style=""color:#e11d2a"">SnapFood</h2><p>{System.Net.WebUtility.HtmlEncode(msg)}</p></body></html>";
}
