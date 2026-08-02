using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Navigation;

/// <summary>
/// Describes a single side-navigation entry and which roles should see it rendered.
/// </summary>
/// <param name="Name">Human-readable navigation item name for test output.</param>
/// <param name="Selector">
/// A CSS selector that uniquely identifies the anchor/element in the rendered navigation, preferring
/// a stable <c>data-testid</c> and otherwise an exact <c>href</c> match.
/// </param>
/// <param name="VisibleToRoles">Authenticated roles that should see the item rendered in the DOM.</param>
/// <param name="VisibleToAnonymous">Whether an unauthenticated visitor should see the item.</param>
/// <param name="Note">Optional note documenting a known nav/controller policy mismatch.</param>
internal sealed record NavigationItem(
    string Name,
    string Selector,
    PortalRoleSet VisibleToRoles,
    bool VisibleToAnonymous = false,
    string? Note = null);

/// <summary>
/// Source-of-truth catalog describing the visibility of every read-only side-navigation entry, keyed
/// off the actual policy gates in <c>_Navigation.cshtml</c>. The <see cref="Helpers.PolicyTagHelper"/>
/// removes unauthorized elements from the rendered output entirely, so tests assert on DOM presence.
/// Two entries intentionally encode a nav/controller policy mismatch (see notes) pending a product
/// decision on the intended behaviour.
/// </summary>
internal static class NavigationItemCatalog
{
    private const PortalRoleSet AllAdmins = PortalRoleSet.AllAuthenticated;
    private const PortalRoleSet GameAdminAndAbove = PortalRoleSet.GameAdmin | PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet HeadAdminAndAbove = PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet SeniorAdminOnly = PortalRoleSet.SeniorAdmin;

    public static IReadOnlyList<NavigationItem> Items { get; } =
    [
        // Public / ungated entries - rendered for everyone including anonymous.
        new("Home", "[data-testid='nav-home']", AllAdmins, VisibleToAnonymous: true),
        new("Servers (toggle)", "[data-testid='nav-servers-toggle']", AllAdmins, VisibleToAnonymous: true),
        new("Servers - Game Servers", "[data-testid='nav-servers-gameservers']", AllAdmins, VisibleToAnonymous: true),
        new("Servers - Player Map", "[data-testid='nav-servers-playermap']", AllAdmins, VisibleToAnonymous: true),
        new("Change Log", "a[href='/ChangeLog']", AllAdmins, VisibleToAnonymous: true),

        // Anonymous-only affordance (inverse check).
        new("Login button", "[data-testid='nav-login-button']", PortalRoleSet.None, VisibleToAnonymous: true),

        // Authenticated affordances.
        new("Profile - Manage", "[data-testid='nav-profile-manage']", AllAdmins),
        new("Logout button", "[data-testid='nav-logout-button']", AllAdmins),

        // FIXED: nav link now public to match MapsController.Index ([AllowAnonymous]).
        new("Servers - Maps", "[data-testid='nav-servers-maps']", AllAdmins, VisibleToAnonymous: true),

        // Dashboard_Read (Moderator and above).
        new("Dashboard", "[data-testid='nav-dashboard']", AllAdmins),
        new("Analytics (toggle)", "[data-testid='nav-analytics-toggle']", AllAdmins),
        new("Analytics - Global", "[data-testid='nav-analytics-global']", AllAdmins),
        new("Analytics - Game", "[data-testid='nav-analytics-game']", AllAdmins),
        new("Analytics - Server", "[data-testid='nav-analytics-server']", AllAdmins),
        new("Analytics - Player", "[data-testid='nav-analytics-player']", AllAdmins),
        new("Analytics - Maps", "[data-testid='nav-analytics-maps']", AllAdmins),

        // AdminActions_Read (Moderator and above).
        new("Admin Actions (toggle)", "a[href='#adminActionsMenu']", AllAdmins),
        new("Admin Actions - My Actions", "a[href='/AdminActions/MyActions']", AllAdmins),
        new("Admin Actions - Global", "a[href='/AdminActions/Global']", AllAdmins),
        new("Admin Actions - Unclaimed", "a[href='/AdminActions/Unclaimed']", AllAdmins),
        new("Connected Players", "a[href='/ConnectedPlayers']", AllAdmins),

        // GameServers_Admin_Read (Moderator and above).
        new("Server Admin (toggle)", "[data-testid='nav-serveradmin-toggle']", AllAdmins),
        new("Server Admin - Dashboard", "[data-testid='nav-serveradmin-dashboard']", AllAdmins),
        new("Server Admin - Server Events", "[data-testid='nav-serveradmin-serverevents']", AllAdmins),

        // GameServers_BanFileMonitors_Read (HeadAdmin and above).
        new("Server Admin - Agent Status", "[data-testid='nav-serveradmin-agentstatus']", HeadAdminAndAbove),
        new("Server Admin - Ban File Monitors", "[data-testid='nav-serveradmin-banfilemonitors']", HeadAdminAndAbove),

        // MapRotations_Read (GameAdmin and above).
        new("Server Admin - Map Rotations", "[data-testid='nav-serveradmin-map-rotations']", GameAdminAndAbove),

        // ChatLog_Read (GameAdmin and above).
        new("Chat Log (toggle)", "[data-testid='nav-chatlog-toggle']", GameAdminAndAbove),
        new("Chat Log - Global", "[data-testid='nav-chatlog-global']", GameAdminAndAbove),

        // Players_Read (Moderator and above).
        new("Players (toggle)", "[data-testid='nav-players-toggle']", AllAdmins),
        new("Players - Global Index", "[data-testid='nav-players-index']", AllAdmins),
        new("Protected Names", "a[href='/ProtectedNames']", AllAdmins),

        // The Credentials nav link and landing page are gated GameServers_Admin_Read
        // (all admin roles). Credential content is filtered per server / per credential
        // type once the page loads (see CredentialsContentVisibilityTests).
        new("Credentials", "[data-testid='nav-credentials']", AllAdmins),

        // Users_Read (HeadAdmin and above).
        new("Users (toggle)", "a[href='#usersMenu']", HeadAdminAndAbove),
        new("Users - Permissions", "a[href='/User/Permissions']", HeadAdminAndAbove),
        new("Users - Permissions Report", "a[href='/User/PermissionsReport']", HeadAdminAndAbove),

        // FIXED: nav link now inherits the parent Users_Read gate (HeadAdmin+) to match UserController.Index.
        new("Users - Manage Users", "a[href='/User']", HeadAdminAndAbove),

        // Users_ActivityLog (SeniorAdmin only).
        new("Users - Activity Log", "a[href='/User/ActivityLog']", SeniorAdminOnly),

        // GameServers_Read (HeadAdmin and above).
        new("Game Servers", "a[href='/GameServers']", HeadAdminAndAbove),

        // GlobalSettings_Admin (SeniorAdmin only).
        new("Global Settings", "[data-testid='nav-global-settings']", SeniorAdminOnly),
        new("Data Maintenance", "[data-testid='nav-data-maintenance']", SeniorAdminOnly),

        // Demos_Read (Moderator and above).
        new("Demo Manager (toggle)", "[data-testid='nav-demos-toggle']", AllAdmins),
        new("Demos - All", "[data-testid='nav-demos-index']", AllAdmins),
        new("Demos - Client", "[data-testid='nav-demos-client']", AllAdmins),

        // Tags_Read (Moderator and above).
        new("Player Tags", "a[href='/Tags']", AllAdmins),
    ];

    /// <summary>
    /// Returns whether the supplied role should see the navigation item rendered.
    /// </summary>
    public static bool IsVisible(NavigationItem item, PortalTestRole role)
    {
        if (role == PortalTestRole.Anonymous)
        {
            return item.VisibleToAnonymous;
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

        return (item.VisibleToRoles & roleFlag) == roleFlag;
    }
}
