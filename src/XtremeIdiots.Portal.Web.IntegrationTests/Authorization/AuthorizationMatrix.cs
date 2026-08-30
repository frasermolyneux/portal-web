using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

[Flags]
internal enum PortalRoleSet
{
    None = 0,
    Moderator = 1,
    GameAdmin = 2,
    HeadAdmin = 4,
    SeniorAdmin = 8,
    AllAuthenticated = Moderator | GameAdmin | HeadAdmin | SeniorAdmin,
}

internal enum PortalTestRole
{
    Anonymous,
    Moderator,
    GameAdmin,
    HeadAdmin,
    SeniorAdmin,
}

internal sealed record AuthorizationMatrixEntry(
    string Policy,
    string Scenario,
    object? Resource,
    PortalRoleSet AllowedRoles,
    bool DirectPermissionAssignable = true);

internal static class AuthorizationMatrix
{
    private readonly static Guid otherUserProfileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly static Guid ownerUserProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly static Guid serverId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string OtherAdminId = "67890";
    private const string OwnerAdminId = "12345";

    private const PortalRoleSet AllAdmins = PortalRoleSet.AllAuthenticated;
    private const PortalRoleSet GameAdminAndAbove = PortalRoleSet.GameAdmin | PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet HeadAdminAndAbove = PortalRoleSet.HeadAdmin | PortalRoleSet.SeniorAdmin;
    private const PortalRoleSet SeniorAdminOnly = PortalRoleSet.SeniorAdmin;

    public static IReadOnlyList<AuthorizationMatrixEntry> Entries { get; } =
    [
        Entry(AuthPolicies.MapRotations_Read, null, GameAdminAndAbove),
        Entry(AuthPolicies.MapRotations_Write, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.MapRotations_Deploy, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.Maps_Read, null, GameAdminAndAbove),

        Entry(AuthPolicies.GameServers_Read, null, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Write, GameType.CallOfDuty4, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Delete, null, SeniorAdminOnly),
        Entry(AuthPolicies.GameServers_Credentials_FileTransport_Read, (GameType.CallOfDuty4, serverId), HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Credentials_FileTransport_Write, GameType.CallOfDuty4, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Credentials_Rcon_Read, (GameType.CallOfDuty4, serverId), GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Credentials_Rcon_Write, GameType.CallOfDuty4, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Maps_Read, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Maps_Deploy, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_BanFileMonitors_Read, null, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_BanFileMonitors_Write, (GameType.CallOfDuty4, serverId), HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Read, null, AllAdmins),
        Entry(AuthPolicies.GameServers_Admin_Rcon, GameType.CallOfDuty4, AllAdmins),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Kick, GameType.CallOfDuty4, AllAdmins),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Ban, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Map, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Say, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Restart, GameType.CallOfDuty4, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Screenshot, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle, GameType.CallOfDuty4x, HeadAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Read, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Delete, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Configure, GameType.CallOfDuty4, GameAdminAndAbove),

        Entry(AuthPolicies.ChatLog_Read, null, GameAdminAndAbove),
        Entry(AuthPolicies.ChatLog_ReadServer, null, AllAdmins),
        Entry(AuthPolicies.ChatLog_Lock, null, GameAdminAndAbove, "without resource"),
        Entry(AuthPolicies.ChatLog_Lock, GameType.CallOfDuty4, AllAdmins, "with game resource"),

        Entry(AuthPolicies.AdminActions_Read, null, AllAdmins),
        Entry(AuthPolicies.AdminActions_Create, (GameType.CallOfDuty4, AdminActionType.Observation), AllAdmins, "observation"),
        Entry(AuthPolicies.AdminActions_Create, (GameType.CallOfDuty4, AdminActionType.Ban), GameAdminAndAbove, "ban"),
        Entry(AuthPolicies.AdminActions_Edit, (GameType.CallOfDuty4, AdminActionType.Observation, OwnerAdminId), AllAdmins, "owned observation"),
        Entry(AuthPolicies.AdminActions_Edit, (GameType.CallOfDuty4, AdminActionType.Observation, OtherAdminId), HeadAdminAndAbove, "other observation"),
        Entry(AuthPolicies.AdminActions_Edit, (GameType.CallOfDuty4, AdminActionType.Ban, OwnerAdminId), GameAdminAndAbove, "owned ban"),
        Entry(AuthPolicies.AdminActions_Edit, (GameType.CallOfDuty4, AdminActionType.Ban, OtherAdminId), HeadAdminAndAbove, "other ban"),
        Entry(AuthPolicies.AdminActions_Delete, null, SeniorAdminOnly),
        Entry(AuthPolicies.AdminActions_Claim, GameType.CallOfDuty4, GameAdminAndAbove),
        Entry(AuthPolicies.AdminActions_Lift, (GameType.CallOfDuty4, OwnerAdminId), GameAdminAndAbove, "owned action"),
        Entry(AuthPolicies.AdminActions_Lift, (GameType.CallOfDuty4, OtherAdminId), HeadAdminAndAbove, "other action"),
        Entry(AuthPolicies.AdminActions_Reassign, GameType.CallOfDuty4, HeadAdminAndAbove),
        Entry(AuthPolicies.AdminActions_CreateTopic, GameType.CallOfDuty4, GameAdminAndAbove),

        Entry(AuthPolicies.Players_Read, null, AllAdmins),
        Entry(AuthPolicies.Players_Delete, null, SeniorAdminOnly),
        Entry(AuthPolicies.Players_ProtectedNames_Write, null, AllAdmins),
        Entry(AuthPolicies.Players_Tags_Write, null, GameAdminAndAbove),
        Entry(AuthPolicies.Tags_Read, null, AllAdmins),
        Entry(AuthPolicies.Tags_Write, null, GameAdminAndAbove),
        Entry(AuthPolicies.Dashboard_Read, null, AllAdmins),

        Entry(AuthPolicies.Demos_Read, null, AllAdmins),
        Entry(AuthPolicies.Demos_Write, null, AllAdmins),
        Entry(AuthPolicies.Demos_Delete, (GameType.CallOfDuty4, ownerUserProfileId), AllAdmins, "owned demo"),
        Entry(AuthPolicies.Demos_Delete, (GameType.CallOfDuty4, otherUserProfileId), HeadAdminAndAbove, "other demo"),

        Entry(AuthPolicies.GlobalSettings_Admin, null, SeniorAdminOnly, directPermissionAssignable: false),
        Entry(AuthPolicies.Users_Read, null, HeadAdminAndAbove, directPermissionAssignable: false),
        Entry(AuthPolicies.Users_ManageClaims, GameType.CallOfDuty4, HeadAdminAndAbove, "with game resource", directPermissionAssignable: false),
        Entry(AuthPolicies.Users_ManageClaims, null, SeniorAdminOnly, "without resource", directPermissionAssignable: false),
        Entry(AuthPolicies.Users_LogOut, null, HeadAdminAndAbove, directPermissionAssignable: false),
        Entry(AuthPolicies.Users_ManageNotificationPreferences, null, SeniorAdminOnly, directPermissionAssignable: false),
        Entry(AuthPolicies.Users_Search, null, AllAdmins, directPermissionAssignable: false),
        Entry(AuthPolicies.Users_ActivityLog, null, SeniorAdminOnly, directPermissionAssignable: false),
    ];

    public static IReadOnlyList<string> NonAssignablePolicies { get; } =
    [
        AuthPolicies.GlobalSettings_Admin,
        AuthPolicies.Users_Read,
        AuthPolicies.Users_ManageClaims,
        AuthPolicies.Users_LogOut,
        AuthPolicies.Users_ManageNotificationPreferences,
        AuthPolicies.Users_Search,
        AuthPolicies.Users_ActivityLog,
    ];

    public static IReadOnlyList<AuthorizationMatrixEntry> PotentialAccessEntries { get; } =
    [
        Entry(AuthPolicies.MapRotations_Write, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.MapRotations_Deploy, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Write, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Credentials_FileTransport_Write, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Credentials_Rcon_Write, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Maps_Read, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Maps_Deploy, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_BanFileMonitors_Write, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon, PotentialAccessProbe.Instance, AllAdmins, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Ban, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Map, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Say, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Restart, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Rcon_Screenshot, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Read, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Delete, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.GameServers_Admin_Screenshots_Configure, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.AdminActions_Claim, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.AdminActions_CreateTopic, PotentialAccessProbe.Instance, GameAdminAndAbove, "potential access"),
        Entry(AuthPolicies.Users_ManageClaims, PotentialAccessProbe.Instance, HeadAdminAndAbove, "potential access", directPermissionAssignable: false),
    ];

    public static bool IsAllowed(AuthorizationMatrixEntry entry, PortalTestRole role)
    {
        var roleFlag = role switch
        {
            PortalTestRole.Anonymous => PortalRoleSet.None,
            PortalTestRole.Moderator => PortalRoleSet.Moderator,
            PortalTestRole.GameAdmin => PortalRoleSet.GameAdmin,
            PortalTestRole.HeadAdmin => PortalRoleSet.HeadAdmin,
            PortalTestRole.SeniorAdmin => PortalRoleSet.SeniorAdmin,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

        return roleFlag != PortalRoleSet.None && entry.AllowedRoles.HasFlag(roleFlag);
    }

    public static AuthorizationMatrixEntry GetEntry(string policy, string scenario)
    {
        return Entries.Single(entry => entry.Policy == policy && entry.Scenario == scenario);
    }

    public static string PermissionValueFor(object? resource)
    {
        return resource switch
        {
            GameType gameType => gameType.ToString(),
            (GameType gameType, Guid) => gameType.ToString(),
            (GameType gameType, string) => gameType.ToString(),
            (GameType gameType, AdminActionType) => gameType.ToString(),
            (GameType gameType, AdminActionType, string) => gameType.ToString(),
            _ => "granted",
        };
    }

    public static bool TryGetResourceGameType(object? resource, out GameType gameType)
    {
        var value = resource switch
        {
            GameType directGameType => directGameType,
            (GameType tupleGameType, Guid) => tupleGameType,
            (GameType tupleGameType, string) => tupleGameType,
            (GameType tupleGameType, AdminActionType) => tupleGameType,
            (GameType tupleGameType, AdminActionType, string) => tupleGameType,
            _ => (GameType?)null,
        };

        gameType = value.GetValueOrDefault();
        return value.HasValue;
    }

    private static AuthorizationMatrixEntry Entry(
        string policy,
        object? resource,
        PortalRoleSet allowedRoles,
        string scenario = "baseline",
        bool directPermissionAssignable = true)
    {
        return new AuthorizationMatrixEntry(policy, scenario, resource, allowedRoles, directPermissionAssignable);
    }
}
