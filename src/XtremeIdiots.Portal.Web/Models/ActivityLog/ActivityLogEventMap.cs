namespace XtremeIdiots.Portal.Web.Models.ActivityLog;

/// <summary>
/// Maps Application Insights custom event names to categories and classifies them as read or write operations
/// </summary>
public static class ActivityLogEventMap
{
    public readonly static Dictionary<string, (ActivityLogCategory Category, bool IsWrite)> Events = new()
    {
        // Authentication
        ["UserLogin"] = (ActivityLogCategory.Authentication, true),
        ["UserLoginFailed"] = (ActivityLogCategory.Authentication, true),
        ["UserLoginLocked"] = (ActivityLogCategory.Authentication, true),
        ["UserLogout"] = (ActivityLogCategory.Authentication, true),

        // Authorization
        ["UnauthorizedUserAccessAttempt"] = (ActivityLogCategory.Authorization, true),
        ["UnauthorizedAccess"] = (ActivityLogCategory.Authorization, true),

        // Admin Actions
        ["AdminActionCreated"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionEdited"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionDeleted"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionClaimed"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionLifted"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionTopicCreated"] = (ActivityLogCategory.AdminActions, true),
        ["AdminActionsDataLoaded"] = (ActivityLogCategory.AdminActions, false),
        ["UnclaimedAdminActionsDataLoaded"] = (ActivityLogCategory.AdminActions, false),
        ["MyAdminActionsDataLoaded"] = (ActivityLogCategory.AdminActions, false),
        ["LatestAdminActionsViewed"] = (ActivityLogCategory.AdminActions, false),

        // Player Management
        ["PlayerKicked"] = (ActivityLogCategory.PlayerManagement, true),
        ["RconPlayerBanned"] = (ActivityLogCategory.PlayerManagement, true),
        ["RconPlayerKicked"] = (ActivityLogCategory.PlayerManagement, true),
        ["RconPlayerTempBanned"] = (ActivityLogCategory.PlayerManagement, true),
        ["PlayerDetailsViewed"] = (ActivityLogCategory.PlayerManagement, false),
        ["PlayersListLoaded"] = (ActivityLogCategory.PlayerManagement, false),
        ["PlayersDataLoaded"] = (ActivityLogCategory.PlayerManagement, false),
        ["IPAddressDetailsViewed"] = (ActivityLogCategory.PlayerManagement, false),

        // Game Servers
        ["GameServerCreated"] = (ActivityLogCategory.GameServers, true),
        ["GameServerUpdated"] = (ActivityLogCategory.GameServers, true),
        ["GameServerDeleted"] = (ActivityLogCategory.GameServers, true),
        ["ServerRestarted"] = (ActivityLogCategory.GameServers, true),
        ["MapRestarted"] = (ActivityLogCategory.GameServers, true),
        ["MapFastRestarted"] = (ActivityLogCategory.GameServers, true),
        ["NextMapTriggered"] = (ActivityLogCategory.GameServers, true),
        ["SayCommandSent"] = (ActivityLogCategory.GameServers, true),
        ["GameServersListAccessed"] = (ActivityLogCategory.GameServers, false),
        ["GameServersBannersRetrieved"] = (ActivityLogCategory.GameServers, false),
        ["GameTrackerBannerRetrieved"] = (ActivityLogCategory.GameServers, false),
        ["GameTrackerBannerFallback"] = (ActivityLogCategory.GameServers, false),

        // Credentials
        ["FtpCredential"] = (ActivityLogCategory.Credentials, false),
        ["RconCredential"] = (ActivityLogCategory.Credentials, false),
        ["CredentialsApiFailure"] = (ActivityLogCategory.Credentials, true),

        // Ban File Monitors
        ["BanFileMonitorCreated"] = (ActivityLogCategory.BanFileMonitors, true),
        ["BanFileMonitorUpdated"] = (ActivityLogCategory.BanFileMonitors, true),
        ["BanFileMonitorDeleted"] = (ActivityLogCategory.BanFileMonitors, true),
        ["BanFileStatusRetrieved"] = (ActivityLogCategory.BanFileMonitors, false),

        // Demos
        ["DemoListLoaded"] = (ActivityLogCategory.Demos, false),

        // Maps
        ["MapDeletedFromHost"] = (ActivityLogCategory.Maps, true),
        ["MapPushedToRemote"] = (ActivityLogCategory.Maps, true),
        ["MapsListRetrieved"] = (ActivityLogCategory.Maps, false),
        ["MapLoaded"] = (ActivityLogCategory.Maps, false),
        ["MapImageRetrieved"] = (ActivityLogCategory.Maps, false),

        // User Management
        ["UserClaimCreated"] = (ActivityLogCategory.UserManagement, true),
        ["UserClaimRemoved"] = (ActivityLogCategory.UserManagement, true),
        ["UserForceLoggedOut"] = (ActivityLogCategory.UserManagement, true),
        ["UserNotificationPreferencesUpdated"] = (ActivityLogCategory.UserManagement, true),
        ["UsersListRetrieved"] = (ActivityLogCategory.UserManagement, false),
        ["UserSearchCompleted"] = (ActivityLogCategory.UserManagement, false),

        // Tags
        ["TagCreated"] = (ActivityLogCategory.Tags, true),
        ["TagUpdated"] = (ActivityLogCategory.Tags, true),
        ["TagDeleted"] = (ActivityLogCategory.Tags, true),

        // Protected Names
        ["ProtectedNameCreated"] = (ActivityLogCategory.ProtectedNames, true),
        ["ProtectedNameDeleted"] = (ActivityLogCategory.ProtectedNames, true),
        ["ProtectedNamesViewed"] = (ActivityLogCategory.ProtectedNames, false),
        ["ProtectedNameReportViewed"] = (ActivityLogCategory.ProtectedNames, false),

        // Chat
        ["ChatMessageLockToggled"] = (ActivityLogCategory.Chat, true),
        ["ChatLogLoaded"] = (ActivityLogCategory.Chat, false),

        // Notifications
        ["AllNotificationsMarkedAsRead"] = (ActivityLogCategory.Notifications, true),
        ["NotificationMarkedAsRead"] = (ActivityLogCategory.Notifications, true),

        // System
        ["HealthCheckPassed"] = (ActivityLogCategory.System, false),
        ["HealthCheckFailed"] = (ActivityLogCategory.System, true),
        ["ChangeLogAccessed"] = (ActivityLogCategory.System, false),
    };

    /// <summary>
    /// Gets all event names belonging to a specific category
    /// </summary>
    public static IReadOnlyList<string> GetEventsByCategory(ActivityLogCategory category)
    {
        return Events
            .Where(e => e.Value.Category == category)
            .Select(e => e.Key)
            .OrderBy(e => e)
            .ToList();
    }

    /// <summary>
    /// Gets all write (mutation) event names
    /// </summary>
    public static IReadOnlyList<string> GetWriteEvents()
    {
        return Events
            .Where(e => e.Value.IsWrite)
            .Select(e => e.Key)
            .OrderBy(e => e)
            .ToList();
    }

    /// <summary>
    /// Gets the category for an event name, or null if unknown
    /// </summary>
    public static ActivityLogCategory? GetCategory(string eventName)
    {
        return Events.TryGetValue(eventName, out var mapping) ? mapping.Category : null;
    }

    /// <summary>
    /// Gets events by category filtered by read/write scope
    /// </summary>
    public static IReadOnlyList<string> GetEventsByCategory(ActivityLogCategory category, bool includeReads)
    {
        return Events
            .Where(e => e.Value.Category == category && (includeReads || e.Value.IsWrite))
            .Select(e => e.Key)
            .OrderBy(e => e)
            .ToList();
    }

    /// <summary>
    /// Gets all categories that have at least one event matching the scope filter
    /// </summary>
    public static IReadOnlyList<ActivityLogCategory> GetActiveCategories(bool includeReads)
    {
        return Events
            .Where(e => includeReads || e.Value.IsWrite)
            .Select(e => e.Value.Category)
            .Distinct()
            .OrderBy(c => c.ToString())
            .ToList();
    }
}
