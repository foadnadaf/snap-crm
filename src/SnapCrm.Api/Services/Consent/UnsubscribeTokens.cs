using System.Security.Cryptography;
using System.Text;

namespace SnapCrm.Api.Services.Consent;

/// <summary>
/// Builds and verifies signed one-click unsubscribe links so nobody can unsubscribe
/// someone else. Token = base64url(email) + "." + base64url(HMAC-SHA256(email)).
/// </summary>
public class UnsubscribeTokens(IConfiguration config)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        config["Crm:UnsubscribeSecret"] ?? "change-me-in-secrets-please-32bytes+");

    public string Create(string email)
    {
        var e = B64(Encoding.UTF8.GetBytes(email));
        var sig = B64(Sign(email));
        return $"{e}.{sig}";
    }

    public bool TryValidate(string token, out string email)
    {
        email = "";
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        try
        {
            var emailBytes = UnB64(parts[0]);
            email = Encoding.UTF8.GetString(emailBytes);
            var expected = Sign(email);
            var actual = UnB64(parts[1]);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }

    private byte[] Sign(string email)
    {
        using var h = new HMACSHA256(_key);
        return h.ComputeHash(Encoding.UTF8.GetBytes(email));
    }

    private static string B64(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
