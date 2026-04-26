namespace XtremeIdiots.Portal.Web.Models.ActivityLog;

/// <summary>
/// Maps Application Insights custom event names (Audit: prefixed) to categories.
/// All events are write/mutation operations — read audits have been removed.
/// </summary>
public static class ActivityLogEventMap
{
    public readonly static Dictionary<string, ActivityLogCategory> Events = new()
    {
        // Authentication
        ["Audit:UserLoggedIn"] = ActivityLogCategory.Authentication,
        ["Audit:UserLogin"] = ActivityLogCategory.Authentication,
        ["Audit:UserLogout"] = ActivityLogCategory.Authentication,

        // Authorization
        ["Audit:UnauthorizedUserAccessAttempt"] = ActivityLogCategory.Authorization,
        ["Audit:UnauthorizedAccess"] = ActivityLogCategory.Authorization,

        // Admin Actions
        ["Audit:AdminActionCreated"] = ActivityLogCategory.AdminActions,
        ["Audit:AdminActionEdited"] = ActivityLogCategory.AdminActions,
        ["Audit:AdminActionDeleted"] = ActivityLogCategory.AdminActions,
        ["Audit:AdminActionClaimed"] = ActivityLogCategory.AdminActions,
        ["Audit:AdminActionLifted"] = ActivityLogCategory.AdminActions,
        ["Audit:AdminActionTopicCreated"] = ActivityLogCategory.AdminActions,

        // Player Management
        ["Audit:RconPlayerKicked"] = ActivityLogCategory.PlayerManagement,
        ["Audit:RconPlayerBanned"] = ActivityLogCategory.PlayerManagement,
        ["Audit:RconPlayerTempBanned"] = ActivityLogCategory.PlayerManagement,
        ["Audit:PlayerTagAdded"] = ActivityLogCategory.PlayerManagement,
        ["Audit:PlayerTagRemoved"] = ActivityLogCategory.PlayerManagement,

        // Game Servers
        ["Audit:GameServerCreated"] = ActivityLogCategory.GameServers,
        ["Audit:GameServerUpdated"] = ActivityLogCategory.GameServers,
        ["Audit:GameServerDeleted"] = ActivityLogCategory.GameServers,
        ["Audit:GameServerConfigChanged"] = ActivityLogCategory.GameServers,
        ["Audit:GameServerToggleChanged"] = ActivityLogCategory.GameServers,
        ["Audit:GameServerOrderUpdated"] = ActivityLogCategory.GameServers,
        ["Audit:ServerRestarted"] = ActivityLogCategory.GameServers,
        ["Audit:MapRestarted"] = ActivityLogCategory.GameServers,
        ["Audit:MapFastRestarted"] = ActivityLogCategory.GameServers,
        ["Audit:NextMapTriggered"] = ActivityLogCategory.GameServers,
        ["Audit:SayCommandSent"] = ActivityLogCategory.GameServers,
        ["Audit:MapLoaded"] = ActivityLogCategory.GameServers,

        // Credentials
        ["Audit:CredentialsAccessed"] = ActivityLogCategory.Credentials,
        ["Audit:CredentialsApiFailure"] = ActivityLogCategory.Credentials,

        // Ban File Monitors
        ["Audit:BanFileMonitorCreated"] = ActivityLogCategory.BanFileMonitors,
        ["Audit:BanFileMonitorUpdated"] = ActivityLogCategory.BanFileMonitors,
        ["Audit:BanFileMonitorDeleted"] = ActivityLogCategory.BanFileMonitors,

        // Demos
        ["Audit:RegenerateAuthKey"] = ActivityLogCategory.Demos,
        ["Audit:ClientDemoUploaded"] = ActivityLogCategory.Demos,
        ["Audit:DemoDeleteViewed"] = ActivityLogCategory.Demos,
        ["Audit:DemoDeleted"] = ActivityLogCategory.Demos,

        // Maps
        ["Audit:MapDeletedFromHost"] = ActivityLogCategory.Maps,
        ["Audit:MapPushedToRemote"] = ActivityLogCategory.Maps,

        // Map Rotations
        ["Audit:MapRotationCreated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationUpdated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationDeleted"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentCreated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentEdited"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentDeleted"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentSynced"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentActivated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentDeactivated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationAssignmentVerified"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationOperationCancelled"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationOrchestrationTerminated"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationImported"] = ActivityLogCategory.MapRotations,
        ["Audit:MapRotationImportConfirmed"] = ActivityLogCategory.MapRotations,

        // User Management
        ["Audit:UserClaimCreated"] = ActivityLogCategory.UserManagement,
        ["Audit:UserClaimRemoved"] = ActivityLogCategory.UserManagement,
        ["Audit:UserForceLoggedOut"] = ActivityLogCategory.UserManagement,
        ["Audit:UserNotificationPreferencesUpdated"] = ActivityLogCategory.UserManagement,

        // Tags
        ["Audit:TagCreated"] = ActivityLogCategory.Tags,
        ["Audit:TagUpdated"] = ActivityLogCategory.Tags,
        ["Audit:TagDeleted"] = ActivityLogCategory.Tags,

        // Protected Names
        ["Audit:ProtectedNameCreated"] = ActivityLogCategory.ProtectedNames,
        ["Audit:ProtectedNameDeleted"] = ActivityLogCategory.ProtectedNames,

        // Chat
        ["Audit:ChatMessageLockToggled"] = ActivityLogCategory.Chat,

        // Global Settings
        ["Audit:GlobalSettingsUpdated"] = ActivityLogCategory.GlobalSettings,

        // Ban File Sync (agent push to game servers + inbound manual ban detection)
        ["Audit:BanFilePushed"] = ActivityLogCategory.BanFileSync,
        ["Audit:BanImported"] = ActivityLogCategory.BanFileSync,
        ["Audit:BanPlayerCreated"] = ActivityLogCategory.BanFileSync,
    };

    /// <summary>
    /// Gets all event names belonging to a specific category
    /// </summary>
    public static IReadOnlyList<string> GetEventsByCategory(ActivityLogCategory category)
    {
        return Events
            .Where(e => e.Value == category)
            .Select(e => e.Key)
            .OrderBy(e => e)
            .ToList();
    }

    /// <summary>
    /// Gets the category for an event name, or null if unknown
    /// </summary>
    public static ActivityLogCategory? GetCategory(string eventName)
    {
        return Events.TryGetValue(eventName, out var category) ? category : null;
    }

    /// <summary>
    /// Gets all categories that have at least one event
    /// </summary>
    public static IReadOnlyList<ActivityLogCategory> GetActiveCategories()
    {
        return Events
            .Select(e => e.Value)
            .Distinct()
            .OrderBy(c => c.ToString())
            .ToList();
    }
}
