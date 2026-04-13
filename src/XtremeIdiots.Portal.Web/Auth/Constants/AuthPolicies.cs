namespace XtremeIdiots.Portal.Web.Auth.Constants;

/// <summary>
/// Contains authorization policy constants used throughout the XtremeIdiots Portal application.
/// Policy names follow the {Domain}.{Action} convention matching AdditionalPermission claim types.
/// Underscores in member names represent dots in the policy string values.
/// </summary>
#pragma warning disable CA1707 // Identifiers should not contain underscores
/// </summary>
public static class AuthPolicies
{
    // Map Rotations
    public const string MapRotations_Read = "MapRotations.Read";
    public const string MapRotations_Write = "MapRotations.Write";
    public const string MapRotations_Deploy = "MapRotations.Deploy";

    // Maps (catalogue)
    public const string Maps_Read = "Maps.Read";

    // Game Servers — Core
    public const string GameServers_Read = "GameServers.Read";
    public const string GameServers_Write = "GameServers.Write";
    public const string GameServers_Delete = "GameServers.Delete";

    // Game Servers — Credentials
    public const string GameServers_Credentials_Ftp_Read = "GameServers.Credentials.Ftp.Read";
    public const string GameServers_Credentials_Ftp_Write = "GameServers.Credentials.Ftp.Write";
    public const string GameServers_Credentials_Rcon_Read = "GameServers.Credentials.Rcon.Read";
    public const string GameServers_Credentials_Rcon_Write = "GameServers.Credentials.Rcon.Write";

    // Game Servers — Maps
    public const string GameServers_Maps_Read = "GameServers.Maps.Read";
    public const string GameServers_Maps_Deploy = "GameServers.Maps.Deploy";

    // Game Servers — Ban File Monitors
    public const string GameServers_BanFileMonitors_Read = "GameServers.BanFileMonitors.Read";
    public const string GameServers_BanFileMonitors_Write = "GameServers.BanFileMonitors.Write";

    // Game Servers — Admin
    public const string GameServers_Admin_Read = "GameServers.Admin.Read";
    public const string GameServers_Admin_Rcon = "GameServers.Admin.Rcon";
    public const string GameServers_Admin_Rcon_Kick = "GameServers.Admin.Rcon.Kick";
    public const string GameServers_Admin_Rcon_Ban = "GameServers.Admin.Rcon.Ban";
    public const string GameServers_Admin_Rcon_Map = "GameServers.Admin.Rcon.Map";
    public const string GameServers_Admin_Rcon_Say = "GameServers.Admin.Rcon.Say";
    public const string GameServers_Admin_Rcon_Restart = "GameServers.Admin.Rcon.Restart";

    // Chat Log
    public const string ChatLog_Read = "ChatLog.Read";
    public const string ChatLog_ReadServer = "ChatLog.ReadServer";
    public const string ChatLog_Lock = "ChatLog.Lock";

    // Admin Actions
    public const string AdminActions_Read = "AdminActions.Read";
    public const string AdminActions_Create = "AdminActions.Create";
    public const string AdminActions_Edit = "AdminActions.Edit";
    public const string AdminActions_Delete = "AdminActions.Delete";
    public const string AdminActions_Claim = "AdminActions.Claim";
    public const string AdminActions_Lift = "AdminActions.Lift";
    public const string AdminActions_Reassign = "AdminActions.Reassign";
    public const string AdminActions_CreateTopic = "AdminActions.CreateTopic";

    // Players
    public const string Players_Read = "Players.Read";
    public const string Players_Delete = "Players.Delete";
    public const string Players_ProtectedNames_Write = "Players.ProtectedNames.Write";
    public const string Players_Tags_Write = "Players.Tags.Write";

    // Player Tags
    public const string Tags_Read = "Tags.Read";
    public const string Tags_Write = "Tags.Write";

    // Dashboard
    public const string Dashboard_Read = "Dashboard.Read";

    // Demos
    public const string Demos_Read = "Demos.Read";
    public const string Demos_Write = "Demos.Write";
    public const string Demos_Delete = "Demos.Delete";

    // Global Settings (not assignable as additional permission)
    public const string GlobalSettings_Admin = "GlobalSettings.Admin";

    // Users (not assignable as additional permissions)
    public const string Users_Read = "Users.Read";
    public const string Users_ManageClaims = "Users.ManageClaims";
    public const string Users_Search = "Users.Search";
    public const string Users_ActivityLog = "Users.ActivityLog";
}