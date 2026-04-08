using System.Security.Cryptography;
using System.Text;

namespace XtremeIdiots.Portal.Web.Services;

public class ExternalTokenService(
    IConfiguration configuration,
    ILogger<ExternalTokenService> logger) : IExternalTokenService
{
    private readonly static TimeSpan tokenExpiry = TimeSpan.FromMinutes(5);

    public ExternalTokenResult ValidateToken(string token)
    {
        try
        {
            var secret = configuration["XtremeIdiots:ExternalWidget:HmacSecret"];
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogError("HMAC secret not configured (XtremeIdiots:ExternalWidget:HmacSecret)");
                return new ExternalTokenResult(false, null, "Token validation not configured");
            }

            // Decode the base64 token
            byte[] tokenBytes;
            try
            {
                tokenBytes = Convert.FromBase64String(token);
            }
            catch (FormatException)
            {
                return new ExternalTokenResult(false, null, "Invalid token format");
            }

            var tokenString = Encoding.UTF8.GetString(tokenBytes);
            var parts = tokenString.Split(':');

            if (parts.Length != 3)
                return new ExternalTokenResult(false, null, "Invalid token structure");

            var forumMemberId = parts[0];
            var timestampStr = parts[1];
            var providedHmac = parts[2];

            // Validate timestamp
            if (!long.TryParse(timestampStr, out var timestampUnix))
                return new ExternalTokenResult(false, null, "Invalid timestamp");

            var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);
            var age = DateTimeOffset.UtcNow - tokenTime;

            if (age > tokenExpiry || age < -TimeSpan.FromMinutes(1))
            {
                logger.LogDebug("Token expired for forum member {ForumMemberId}, age: {Age}", forumMemberId, age);
                return new ExternalTokenResult(false, null, "Token expired");
            }

            // Validate HMAC
            var payload = $"{forumMemberId}:{timestampStr}";
            var expectedHmac = ComputeHmac(secret, payload);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedHmac),
                Encoding.UTF8.GetBytes(expectedHmac)))
            {
                logger.LogWarning("Invalid HMAC signature for forum member {ForumMemberId}", forumMemberId);
                return new ExternalTokenResult(false, null, "Invalid signature");
            }

            return new ExternalTokenResult(true, forumMemberId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating external token");
            return new ExternalTokenResult(false, null, "Validation error");
        }
    }

    private static string ComputeHmac(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
