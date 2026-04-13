using Microsoft.AspNetCore.Authorization;

using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class PolicyExtensions
{
    public static void AddXtremeIdiotsPolicies(this AuthorizationOptions options)
    {
        // Map Rotations
        options.AddPolicy(AuthPolicies.MapRotations_Read, policy => policy.Requirements.Add(new MapRotationsRead()));
        options.AddPolicy(AuthPolicies.MapRotations_Write, policy => policy.Requirements.Add(new MapRotationsWrite()));
        options.AddPolicy(AuthPolicies.MapRotations_Deploy, policy => policy.Requirements.Add(new MapRotationsDeploy()));

        // Maps
        options.AddPolicy(AuthPolicies.Maps_Read, policy => policy.Requirements.Add(new MapsRead()));

        // Game Servers — Core
        options.AddPolicy(AuthPolicies.GameServers_Read, policy => policy.Requirements.Add(new GameServersRead()));
        options.AddPolicy(AuthPolicies.GameServers_Write, policy => policy.Requirements.Add(new GameServersWrite()));
        options.AddPolicy(AuthPolicies.GameServers_Delete, policy => policy.Requirements.Add(new GameServersDelete()));

        // Game Servers — Credentials
        options.AddPolicy(AuthPolicies.GameServers_Credentials_Ftp_Read, policy => policy.Requirements.Add(new GameServersCredentialsFtpRead()));
        options.AddPolicy(AuthPolicies.GameServers_Credentials_Ftp_Write, policy => policy.Requirements.Add(new GameServersCredentialsFtpWrite()));
        options.AddPolicy(AuthPolicies.GameServers_Credentials_Rcon_Read, policy => policy.Requirements.Add(new GameServersCredentialsRconRead()));
        options.AddPolicy(AuthPolicies.GameServers_Credentials_Rcon_Write, policy => policy.Requirements.Add(new GameServersCredentialsRconWrite()));

        // Game Servers — Maps
        options.AddPolicy(AuthPolicies.GameServers_Maps_Read, policy => policy.Requirements.Add(new GameServersMapsRead()));
        options.AddPolicy(AuthPolicies.GameServers_Maps_Deploy, policy => policy.Requirements.Add(new GameServersMapsDeploy()));

        // Game Servers — Ban File Monitors
        options.AddPolicy(AuthPolicies.GameServers_BanFileMonitors_Read, policy => policy.Requirements.Add(new GameServersBanFileMonitorsRead()));
        options.AddPolicy(AuthPolicies.GameServers_BanFileMonitors_Write, policy => policy.Requirements.Add(new GameServersBanFileMonitorsWrite()));

        // Game Servers — Admin
        options.AddPolicy(AuthPolicies.GameServers_Admin_Read, policy => policy.Requirements.Add(new GameServersAdminRead()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon, policy => policy.Requirements.Add(new GameServersAdminRcon()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon_Kick, policy => policy.Requirements.Add(new GameServersAdminRconKick()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon_Ban, policy => policy.Requirements.Add(new GameServersAdminRconBan()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon_Map, policy => policy.Requirements.Add(new GameServersAdminRconMap()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon_Say, policy => policy.Requirements.Add(new GameServersAdminRconSay()));
        options.AddPolicy(AuthPolicies.GameServers_Admin_Rcon_Restart, policy => policy.Requirements.Add(new GameServersAdminRconRestart()));

        // Chat Log
        options.AddPolicy(AuthPolicies.ChatLog_Read, policy => policy.Requirements.Add(new ChatLogRead()));
        options.AddPolicy(AuthPolicies.ChatLog_ReadServer, policy => policy.Requirements.Add(new ChatLogReadServer()));
        options.AddPolicy(AuthPolicies.ChatLog_Lock, policy => policy.Requirements.Add(new ChatLogLock()));

        // Admin Actions
        options.AddPolicy(AuthPolicies.AdminActions_Read, policy => policy.Requirements.Add(new AdminActionsRead()));
        options.AddPolicy(AuthPolicies.AdminActions_Create, policy => policy.Requirements.Add(new AdminActionsCreate()));
        options.AddPolicy(AuthPolicies.AdminActions_Edit, policy => policy.Requirements.Add(new AdminActionsEdit()));
        options.AddPolicy(AuthPolicies.AdminActions_Delete, policy => policy.Requirements.Add(new AdminActionsDelete()));
        options.AddPolicy(AuthPolicies.AdminActions_Claim, policy => policy.Requirements.Add(new AdminActionsClaim()));
        options.AddPolicy(AuthPolicies.AdminActions_Lift, policy => policy.Requirements.Add(new AdminActionsLift()));
        options.AddPolicy(AuthPolicies.AdminActions_Reassign, policy => policy.Requirements.Add(new AdminActionsReassign()));
        options.AddPolicy(AuthPolicies.AdminActions_CreateTopic, policy => policy.Requirements.Add(new AdminActionsCreateTopic()));

        // Players
        options.AddPolicy(AuthPolicies.Players_Read, policy => policy.Requirements.Add(new PlayersRead()));
        options.AddPolicy(AuthPolicies.Players_Delete, policy => policy.Requirements.Add(new PlayersDelete()));
        options.AddPolicy(AuthPolicies.Players_ProtectedNames_Write, policy => policy.Requirements.Add(new PlayersProtectedNamesWrite()));
        options.AddPolicy(AuthPolicies.Players_Tags_Write, policy => policy.Requirements.Add(new PlayersTagsWrite()));

        // Tags
        options.AddPolicy(AuthPolicies.Tags_Read, policy => policy.Requirements.Add(new TagsRead()));
        options.AddPolicy(AuthPolicies.Tags_Write, policy => policy.Requirements.Add(new TagsWrite()));

        // Dashboard
        options.AddPolicy(AuthPolicies.Dashboard_Read, policy => policy.Requirements.Add(new DashboardRead()));

        // Demos
        options.AddPolicy(AuthPolicies.Demos_Read, policy => policy.Requirements.Add(new DemosRead()));
        options.AddPolicy(AuthPolicies.Demos_Write, policy => policy.Requirements.Add(new DemosWrite()));
        options.AddPolicy(AuthPolicies.Demos_Delete, policy => policy.Requirements.Add(new DemosDelete()));

        // Global Settings
        options.AddPolicy(AuthPolicies.GlobalSettings_Admin, policy => policy.Requirements.Add(new GlobalSettingsAdmin()));

        // Users
        options.AddPolicy(AuthPolicies.Users_Read, policy => policy.Requirements.Add(new UsersRead()));
        options.AddPolicy(AuthPolicies.Users_ManageClaims, policy => policy.Requirements.Add(new UsersManageClaims()));
        options.AddPolicy(AuthPolicies.Users_Search, policy => policy.Requirements.Add(new UsersSearch()));
        options.AddPolicy(AuthPolicies.Users_ActivityLog, policy => policy.Requirements.Add(new UsersActivityLog()));
    }
}