using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

/// <summary>
/// Shared mapping between the baseline <see cref="PortalTestRole"/> values and the
/// authentication profile header value used to impersonate them over HTTP or in the browser.
/// Reused by both the HTTP feature-access tests and the Playwright navigation tests so that
/// the role list stays in one place as coverage scales across the solution.
/// </summary>
internal static class TestRoles
{
    /// <summary>
    /// The five baseline roles exercised for read-only feature and navigation coverage.
    /// </summary>
    public static IReadOnlyList<PortalTestRole> Baseline { get; } =
    [
        PortalTestRole.Anonymous,
        PortalTestRole.Moderator,
        PortalTestRole.GameAdmin,
        PortalTestRole.HeadAdmin,
        PortalTestRole.SeniorAdmin,
    ];

    /// <summary>
    /// Resolves the test authentication profile header value for a role, or <see langword="null"/>
    /// for <see cref="PortalTestRole.Anonymous"/> (no header sent).
    /// </summary>
    public static string? ProfileFor(PortalTestRole role)
    {
        return role switch
        {
            PortalTestRole.Anonymous => null,
            PortalTestRole.Moderator => TestPrincipalProfiles.Moderator,
            PortalTestRole.GameAdmin => TestPrincipalProfiles.GameAdmin,
            PortalTestRole.HeadAdmin => TestPrincipalProfiles.HeadAdmin,
            PortalTestRole.SeniorAdmin => TestPrincipalProfiles.SeniorAdmin,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }
}
