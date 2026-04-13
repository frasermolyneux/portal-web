using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Consolidated authorization handler for all GameServers.* policies including core,
/// credentials, maps, ban file monitors, and admin operations.
/// </summary>
public class GameServersAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                // Core
                case GameServersRead:
                    HandleRead(context, requirement);
                    break;
                case GameServersWrite:
                    HandleWrite(context, requirement);
                    break;
                case GameServersDelete:
                    HandleDelete(context, requirement);
                    break;

                // Credentials
                case GameServersCredentialsFtpRead:
                    HandleCredentialsFtpRead(context, requirement);
                    break;
                case GameServersCredentialsFtpWrite:
                    HandleCredentialsFtpWrite(context, requirement);
                    break;
                case GameServersCredentialsRconRead:
                    HandleCredentialsRconRead(context, requirement);
                    break;
                case GameServersCredentialsRconWrite:
                    HandleCredentialsRconWrite(context, requirement);
                    break;

                // Maps
                case GameServersMapsRead:
                    HandleMapsRead(context, requirement);
                    break;
                case GameServersMapsDeploy:
                    HandleMapsDeploy(context, requirement);
                    break;

                // Ban File Monitors
                case GameServersBanFileMonitorsRead:
                    HandleBanFileMonitorsRead(context, requirement);
                    break;
                case GameServersBanFileMonitorsWrite:
                    HandleBanFileMonitorsWrite(context, requirement);
                    break;

                // Admin
                case GameServersAdminRead:
                    HandleAdminRead(context, requirement);
                    break;
                case GameServersAdminRcon:
                    HandleAdminRcon(context, requirement);
                    break;
                case GameServersAdminRconKick:
                    HandleAdminRconKick(context, requirement);
                    break;
                case GameServersAdminRconBan:
                    HandleAdminRconBan(context, requirement);
                    break;
                case GameServersAdminRconMap:
                    HandleAdminRconMap(context, requirement);
                    break;
                case GameServersAdminRconSay:
                    HandleAdminRconSay(context, requirement);
                    break;
                case GameServersAdminRconRestart:
                    HandleAdminRconRestart(context, requirement);
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }

    #region Core

    private static void HandleRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.GameServerAccessLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Read");
    }

    private static void HandleWrite(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
        if (context.Resource is GameType gameType)
            BaseAuthorizationHelper.CheckCombinedGameServerAccess(context, requirement, gameType);
        else if (context.Resource is PotentialAccessProbe)
        {
            // Mirror CheckCombinedGameServerAccess: HeadAdmin or GameServers.Read for any game type
            if (context.User.Claims.Any(c =>
                (c.Type == UserProfileClaimType.HeadAdmin || c.Type == AdditionalPermission.GameServers_Read) &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Write");
    }

    private static void HandleDelete(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Delete");
    }

    #endregion

    #region Credentials

    private static void HandleCredentialsFtpRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, Guid> refTuple)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, refTuple.Item1);
            if (!context.HasSucceeded)
                BaseAuthorizationHelper.CheckFtpCredentialsAccess(context, requirement, refTuple.Item2);
        }
        else if (context.Resource is (GameType gameType, Guid gameServerId))
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
            if (!context.HasSucceeded)
                BaseAuthorizationHelper.CheckFtpCredentialsAccess(context, requirement, gameServerId);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Credentials.Ftp.Read");
    }

    private static void HandleCredentialsFtpWrite(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrHeadAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Credentials.Ftp.Write");
    }

    private static void HandleCredentialsRconRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, Guid> refTuple)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, refTuple.Item1);
            if (!context.HasSucceeded)
            {
                BaseAuthorizationHelper.CheckGameAdminAccess(context, requirement, refTuple.Item1);
                BaseAuthorizationHelper.CheckRconCredentialsAccess(context, requirement, refTuple.Item2);
            }
        }
        else if (context.Resource is (GameType gameType, Guid gameServerId))
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
            if (!context.HasSucceeded)
            {
                BaseAuthorizationHelper.CheckGameAdminAccess(context, requirement, gameType);
                BaseAuthorizationHelper.CheckRconCredentialsAccess(context, requirement, gameServerId);
            }
        }
        else if (context.Resource is GameType singleGameType)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, singleGameType);
            if (!context.HasSucceeded)
                BaseAuthorizationHelper.CheckGameAdminAccess(context, requirement, singleGameType);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Credentials.Rcon.Read");
    }

    private static void HandleCredentialsRconWrite(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrHeadAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Credentials.Rcon.Write");
    }

    #endregion

    #region Maps

    private static void HandleMapsRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Maps.Read");
    }

    private static void HandleMapsDeploy(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Maps.Deploy");
    }

    #endregion

    #region Ban File Monitors

    private static void HandleBanFileMonitorsRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.BanFileMonitorLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.BanFileMonitors.Read");
    }

    private static void HandleBanFileMonitorsWrite(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameTypeServerAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.BanFileMonitors.Write");
    }

    #endregion

    #region Admin

    private static void HandleAdminRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.ServerAdminAccessLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Read");
    }

    private static void HandleAdminRcon(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrLiveRconAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon");
    }

    private static void HandleAdminRconKick(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon.Kick");
    }

    private static void HandleAdminRconBan(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon.Ban");
    }

    private static void HandleAdminRconMap(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon.Map");
    }

    private static void HandleAdminRconSay(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon.Say");
    }

    private static void HandleAdminRconRestart(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrHeadAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "GameServers.Admin.Rcon.Restart");
    }

    #endregion
}