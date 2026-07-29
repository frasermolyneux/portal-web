using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

internal static class TestPrincipalProfiles
{
    public const string GameAdmin = "game-admin";
    public const string HeadAdmin = "head-admin";
    public const string Moderator = "moderator";
    public const string SeniorAdmin = "senior-admin";

    public static ClaimsPrincipal Create(string profile)
    {
        var claims = CreateIdentityClaims();

        claims.Add(profile switch
        {
            GameAdmin => new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()),
            HeadAdmin => new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()),
            Moderator => new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
            SeniorAdmin => new Claim(UserProfileClaimType.SeniorAdmin, bool.TrueString),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown test principal profile."),
        });

        return new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
    }

    private static List<Claim> CreateIdentityClaims()
    {
        return
        [
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Portal Test User"),
            new Claim(ClaimTypes.Email, "portal-test@example.invalid"),
            new Claim(UserProfileClaimType.XtremeIdiotsId, "12345"),
            new Claim(UserProfileClaimType.UserProfileId, "11111111-1111-1111-1111-111111111111"),
            new Claim(UserProfileClaimType.PhotoUrl, "/images/noimage.jpg"),
        ];
    }
}
