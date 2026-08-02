using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

/// <summary>
/// Describes a single read-only (safe GET) landing route for a web feature and the roles that
/// should be able to reach it.
/// </summary>
/// <param name="Name">Human-readable feature name for test output.</param>
/// <param name="Route">Absolute request path with no required route parameters.</param>
/// <param name="AllowedRoles">
/// Authenticated roles that are authorized to reach the controller action. Ignored for anonymous
/// access, which is driven by <paramref name="AllowAnonymous"/>.
/// </param>
/// <param name="AllowAnonymous">Whether an unauthenticated visitor may reach the action.</param>
/// <param name="RendersUnderMock">
/// Whether the page renders a 200 HTML response for an authorized role against the mocked backend
/// test host. Pages that currently error under <c>DefaultValue.Mock</c> data are flagged
/// <see langword="false"/>; the access boundary is still validated for these, but the strict
/// render assertion is skipped. This reflects the mocked test host, not production behaviour.
/// </param>
/// <param name="AuthorizedRedirectPath">
/// Optional safe landing path for authorized roles whose requested route redirects based on scope.
/// When set, the render test accepts either a 200 response or a redirect to this exact path.
/// </param>
internal sealed record ReadOnlyFeature(
    string Name,
    string Route,
    PortalRoleSet AllowedRoles,
    bool AllowAnonymous = false,
    bool RendersUnderMock = true,
    string? AuthorizedRedirectPath = null);

/// <summary>
/// Source-of-truth catalog of read-only web feature landing pages and their expected access.
/// Grows as read-only coverage extends across the solution. Expected role-sets mirror
/// <see cref="AuthorizationMatrix"/> which is validated independently at the handler level.
/// </summary>
internal static class ReadOnlyFeatureCatalog
{
    private const PortalRoleSet AllAdmins = PortalRoleSet.AllAuthenticated;
    private const PortalRoleSet GameAdminAndAbove = PortalRoleSet.GameAdmin | PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet HeadAdminAndAbove = PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet SeniorAdminOnly = PortalRoleSet.SeniorAdmin;

    public static IReadOnlyList<ReadOnlyFeature> Features { get; } =
    [
        // Public pages - reachable by everyone including anonymous.
        new("Home", "/", AllAdmins, AllowAnonymous: true, RendersUnderMock: false),
        new("Change Log", "/ChangeLog", AllAdmins, AllowAnonymous: true),
        new("Servers - Game Servers", "/Servers", AllAdmins, AllowAnonymous: true, RendersUnderMock: false),
        new("Servers - Player Map", "/Servers/Map", AllAdmins, AllowAnonymous: true, RendersUnderMock: false),
        new("Maps", "/Maps", AllAdmins, AllowAnonymous: true),

        // Dashboard_Read (Moderator and above).
        new("Dashboard", "/Dashboard", AllAdmins),
        new("Analytics", "/Analytics", AllAdmins),
        new("Analytics - Global", "/Analytics/Global", AllAdmins),
        new("Analytics - Game", "/Analytics/Game", AllAdmins),
        new("Analytics - Server", "/Analytics/Server", AllAdmins),
        new("Analytics - Player", "/Analytics/Player", AllAdmins),
        new("Analytics - Maps", "/Analytics/Maps", AllAdmins),

        // AdminActions_Read (Moderator and above).
        new("Admin Actions - My Actions", "/AdminActions/MyActions", AllAdmins, RendersUnderMock: false),
        new("Admin Actions - Global", "/AdminActions/Global", AllAdmins),
        new("Admin Actions - Unclaimed", "/AdminActions/Unclaimed", AllAdmins),
        new("Connected Players", "/ConnectedPlayers", AllAdmins),

        // GameServers_Admin_Read (Moderator and above).
        new("Server Admin - Dashboard", "/ServerAdmin", AllAdmins, RendersUnderMock: false),
        new("Server Admin - Server Events", "/ServerAdmin/ServerEventsIndex", AllAdmins),

        // ChatLog_Read (GameAdmin and above).
        new("Server Admin - Global Chat Log", "/ServerAdmin/ChatLogIndex", GameAdminAndAbove),

        // MapRotations_Read (GameAdmin and above).
        new("Map Rotations", "/MapRotations", GameAdminAndAbove),

        // GameServers_BanFileMonitors_Read (HeadAdmin and above).
        new("Agent Status", "/Status/AgentStatus", HeadAdminAndAbove),
        new("Ban File Monitors", "/BanFileMonitors", HeadAdminAndAbove, RendersUnderMock: false),

        // GameServers_Read (HeadAdmin and above).
        new("Game Servers", "/GameServers", HeadAdminAndAbove, RendersUnderMock: false),

        // GameServers_Admin_Read (Moderator and above). The Credentials landing page is
        // reachable by every admin role; the per-server/per-credential-type content is
        // strictly filtered once loaded (covered by CredentialsContentVisibilityTests).
        new("Credentials", "/Credentials", AllAdmins, RendersUnderMock: false),

        // Players_Read (Moderator and above).
        new("Players - Global Index", "/Players", AllAdmins, AuthorizedRedirectPath: "/Players/GameIndex/CallOfDuty4"),
        new("Protected Names", "/ProtectedNames", AllAdmins, RendersUnderMock: false),

        // Users_Read (HeadAdmin and above).
        new("Users - Manage", "/User", HeadAdminAndAbove),
        new("Users - Permissions", "/User/Permissions", HeadAdminAndAbove),
        new("Users - Permissions Report", "/User/PermissionsReport", HeadAdminAndAbove),

        // Users_ActivityLog (SeniorAdmin only).
        new("Users - Activity Log", "/User/ActivityLog", SeniorAdminOnly),

        // GlobalSettings_Admin (SeniorAdmin only).
        new("Global Settings", "/GlobalSettings", SeniorAdminOnly),
        new("Data Maintenance", "/DataMaintenance", SeniorAdminOnly),

        // Demos_Read (Moderator and above).
        new("Demos - All", "/Demos", AllAdmins),
        new("Demos - Client", "/Demos/DemoClient", AllAdmins, RendersUnderMock: false),

        // Tags_Read (Moderator and above).
        new("Player Tags", "/Tags", AllAdmins, RendersUnderMock: false),

        // [Authorize] - any authenticated user.
        new("Profile - Manage", "/Profile/Manage", AllAdmins),
    ];

    /// <summary>
    /// Returns whether the supplied role should be able to reach the feature's controller action.
    /// </summary>
    public static bool IsAllowed(ReadOnlyFeature feature, PortalTestRole role)
    {
        if (role == PortalTestRole.Anonymous)
        {
            return feature.AllowAnonymous;
        }

        var roleFlag = role switch
        {
            PortalTestRole.Anonymous => PortalRoleSet.None,
            PortalTestRole.Moderator => PortalRoleSet.Moderator,
            PortalTestRole.GameAdmin => PortalRoleSet.GameAdmin,
            PortalTestRole.HeadAdmin => PortalRoleSet.HeadAdmin,
            PortalTestRole.SeniorAdmin => PortalRoleSet.SeniorAdmin,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

        return (feature.AllowedRoles & roleFlag) == roleFlag;
    }
}
