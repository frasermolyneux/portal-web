using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Provides common authorization helper methods for policy-based authorization throughout the XtremeIdiots Portal
/// </summary>
public static class BaseAuthorizationHelper
{
    #region Constants

    /// <summary>
    /// Predefined claim groups for different authorization levels and access patterns
    /// </summary>
    public static class ClaimGroups
    {
        public readonly static string[] SeniorAdminOnly = [UserProfileClaimType.SeniorAdmin];

        public readonly static string[] AllAdminLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin,
            UserProfileClaimType.Moderator
        ];

        public readonly static string[] BanFileMonitorLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            AdditionalPermission.GameServers_BanFileMonitors_Read
        ];

        public readonly static string[] CredentialsAccessLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin,
            AuthPolicies.GameServers_Credentials_FileTransport_Read,
            AdditionalPermission.GameServers_Credentials_Rcon_Read
        ];

        public readonly static string[] GameServerAccessLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            AdditionalPermission.GameServers_Read
        ];

        public readonly static string[] AdminLevelsExcludingModerators =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin
        ];

        public readonly static string[] ServerAdminAccessLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin,
            UserProfileClaimType.Moderator,
            AdditionalPermission.GameServers_Admin_Read
        ];

        public readonly static string[] LiveRconAccessLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin,
            AdditionalPermission.GameServers_Admin_Rcon
        ];

        public readonly static string[] SeniorAndHeadAdminOnly =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin
        ];

        public readonly static string[] StatusAccessLevels =
        [
            UserProfileClaimType.SeniorAdmin,
            UserProfileClaimType.HeadAdmin,
            UserProfileClaimType.GameAdmin,
            AdditionalPermission.GameServers_BanFileMonitors_Read
        ];
    }

    #endregion

    #region Core Authorization Checks

    /// <summary>
    /// Checks if the user has senior admin privileges (highest level access)
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorAdminAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks if the user has any of the specified claim types
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="claimTypes">Array of claim types to check for</param>
    public static void CheckClaimTypes(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, string[] claimTypes)
    {
        if (context.User.Claims.Any(claim => claimTypes.Contains(claim.Type)))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks if the user has head admin access for the specified game type
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckHeadAdminAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        if (HasGameScopedClaim(context.User, UserProfileClaimType.HeadAdmin, gameType))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks if the user has game admin access for the specified game type (includes head admin level)
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckGameAdminAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        if (HasGameScopedClaim(context.User, UserProfileClaimType.HeadAdmin, gameType) ||
            HasGameScopedClaim(context.User, UserProfileClaimType.GameAdmin, gameType))
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Checks if the user has moderator access for the specified game type (includes game admin level)
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckModeratorAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        if (HasGameScopedClaim(context.User, UserProfileClaimType.Moderator, gameType) ||
            HasGameScopedClaim(context.User, UserProfileClaimType.GameAdmin, gameType))
        {
            context.Succeed(requirement);
        }
    }

    #endregion

    #region Composite Authorization Checks

    /// <summary>
    /// Checks senior admin access first, then game admin access based on resource context
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorOrGameAdminAccessWithResource(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is GameType gameType)
            CheckGameAdminAccess(context, requirement, gameType);
        else if (context.Resource is PotentialAccessProbe)
        {
            if (context.User.Claims.Any(c =>
                (c.Type == UserProfileClaimType.HeadAdmin || c.Type == UserProfileClaimType.GameAdmin) &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Checks senior admin access first, then head admin access based on resource context
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorOrHeadAdminAccessWithResource(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is GameType gameType)
            CheckHeadAdminAccess(context, requirement, gameType);
        else if (context.Resource is PotentialAccessProbe)
        {
            if (context.User.Claims.Any(c =>
                c.Type == UserProfileClaimType.HeadAdmin &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Checks senior admin access first, then game type and server specific access
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorOrGameTypeServerAccessWithResource(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is Tuple<GameType, Guid> refTuple)
        {
            var gameType = refTuple.Item1;
            var gameServerId = refTuple.Item2;
            CheckGameTypeAndServerAccess(context, requirement, gameType, gameServerId);
        }
        else if (context.Resource is (GameType gameType, Guid gameServerId))
        {
            CheckGameTypeAndServerAccess(context, requirement, gameType, gameServerId);
        }
        else if (context.Resource is PotentialAccessProbe)
        {
            if (context.User.Claims.Any(c =>
                c.Type == UserProfileClaimType.HeadAdmin &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Checks senior admin access first, then multiple levels of game access
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorOrMultipleGameAccessWithResource(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is GameType gameType)
        {
            CheckHeadAdminAccess(context, requirement, gameType);
            CheckGameAdminAccess(context, requirement, gameType);
            CheckModeratorAccess(context, requirement, gameType);
        }
        else if (context.Resource is PotentialAccessProbe)
        {
            if (context.User.Claims.Any(c =>
                (c.Type == UserProfileClaimType.HeadAdmin || c.Type == UserProfileClaimType.GameAdmin || c.Type == UserProfileClaimType.Moderator) &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Checks senior admin access first, then live RCON access for the game type
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckSeniorOrLiveRconAccessWithResource(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Claims.Any(claim => claim.Type == UserProfileClaimType.SeniorAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is GameType gameType)
        {
            CheckGameAdminAccess(context, requirement, gameType);
            CheckLiveRconAccess(context, requirement, gameType);
        }
        else if (context.Resource is PotentialAccessProbe)
        {
            if (context.User.Claims.Any(c =>
                (c.Type == UserProfileClaimType.HeadAdmin || c.Type == UserProfileClaimType.GameAdmin || c.Type == AdditionalPermission.GameServers_Admin_Rcon) &&
                Enum.TryParse<GameType>(c.Value, out _)))
                context.Succeed(requirement);
        }
    }

    #endregion

    #region Server-Specific Authorization

    /// <summary>
    /// Checks if the user has ban file monitor access for a specific game server
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameServerId">The game server ID to check permissions for</param>
    public static void CheckBanFileMonitorAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, Guid gameServerId)
    {
        if (context.User.HasClaim(AdditionalPermission.GameServers_BanFileMonitors_Read, gameServerId.ToString()))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks both game type and server-specific access permissions
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    /// <param name="gameServerId">The game server ID to check permissions for</param>
    public static void CheckGameTypeAndServerAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType, Guid gameServerId)
    {
        CheckHeadAdminAccess(context, requirement, gameType);
        CheckBanFileMonitorAccess(context, requirement, gameServerId);
    }

    #endregion

    #region Game Server Authorization

    /// <summary>
    /// Checks if the user has game server access for the specified game type
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckGameServerAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        if (HasGameScopedClaim(context.User, AdditionalPermission.GameServers_Read, gameType))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks if the user has RCON credentials access for a specific game server
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameServerId">The game server ID to check permissions for</param>
    public static void CheckRconCredentialsAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, Guid gameServerId)
    {
        if (context.User.HasClaim(AdditionalPermission.GameServers_Credentials_Rcon_Read, gameServerId.ToString()))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks if the user has file transport credentials access for a specific game server.
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameServerId">The game server ID to check permissions for</param>
    public static void CheckFileTransportCredentialsAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, Guid gameServerId)
    {
        if (context.User.HasClaim(AuthPolicies.GameServers_Credentials_FileTransport_Read, gameServerId.ToString()))
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Combines head admin and game server access checks for comprehensive server access
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckCombinedGameServerAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        CheckHeadAdminAccess(context, requirement, gameType);
        CheckGameServerAccess(context, requirement, gameType);
    }

    /// <summary>
    /// Checks if the user has live RCON access for the specified game type
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    /// <param name="gameType">The game type to check permissions for</param>
    public static void CheckLiveRconAccess(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType)
    {
        if (HasGameScopedClaim(context.User, AdditionalPermission.GameServers_Admin_Rcon, gameType))
            context.Succeed(requirement);
    }

    #endregion

    #region Direct Permission Grant

    /// <summary>
    /// Checks if the user has a direct additional permission grant matching the policy and resource.
    /// Extracts the GameType from the resource (regardless of tuple shape) and checks for a matching claim.
    /// </summary>
    public static void CheckDirectPermissionGrant(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, string permissionClaimType)
    {
        if (context.HasSucceeded)
            return;

        // Extract GameType from resource — handles all tuple patterns used across handlers
        var resourceGameType = context.Resource switch
        {
            GameType gt => gt,
            Tuple<GameType, Guid> t => t.Item1,
            Tuple<GameType, string> t => t.Item1,
            Tuple<GameType, AdminActionType> t => t.Item1,
            Tuple<GameType, AdminActionType, string?> t => t.Item1,
            (GameType gt, Guid) => gt,
            (GameType gt, string) => gt,
            (GameType gt, AdminActionType) => gt,
            (GameType gt, AdminActionType, string) => gt,
            _ => (GameType?)null
        };

        // Extract server GUID if present (for server-scoped permissions)
        var resourceServerId = context.Resource switch
        {
            Tuple<GameType, Guid> t => t.Item2,
            (GameType, Guid id) => id,
            _ => (Guid?)null
        };

        if (resourceGameType.HasValue)
        {
            // Check game-scoped permission
            if (HasGameScopedClaim(context.User, permissionClaimType, resourceGameType.Value))
                context.Succeed(requirement);

            // Also check server-scoped permission if a server ID is present
            if (resourceServerId.HasValue &&
                context.User.HasClaim(permissionClaimType, resourceServerId.Value.ToString()))
                context.Succeed(requirement);

            return;
        }

        // No resource: check if user has the permission claim with ANY value
        if (context.User.Claims.Any(c => c.Type == permissionClaimType))
            context.Succeed(requirement);
    }

    /// <summary>
    /// Checks for a game-scoped claim, including known equivalent game mappings.
    /// </summary>
    public static bool HasGameScopedClaim(ClaimsPrincipal user, string claimType, GameType gameType)
    {
        return GetEquivalentGameTypes(gameType)
            .Any(equivalentGameType => user.HasClaim(claimType, equivalentGameType.ToString()));
    }

    private static IReadOnlyList<GameType> GetEquivalentGameTypes(GameType gameType)
    {
        return gameType == GameType.CallOfDuty4
            ? [GameType.CallOfDuty4, GameType.CallOfDuty4x]
            : gameType == GameType.CallOfDuty4x
                ? [GameType.CallOfDuty4x, GameType.CallOfDuty4]
                : [gameType];
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Retrieves the XtremeIdiots ID from the user's claims
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <returns>The XtremeIdiots ID if found, otherwise null</returns>
    public static string? GetUserXtremeIdiotsId(AuthorizationHandlerContext context)
    {
        return context.User.FindFirst(UserProfileClaimType.XtremeIdiotsId)?.Value;
    }

    /// <summary>
    /// Determines if the current user is the owner of the specified action
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="adminId">The admin ID to compare against the user's ID</param>
    /// <returns>True if the user is the action owner, false otherwise</returns>
    public static bool IsActionOwner(AuthorizationHandlerContext context, string? adminId)
    {
        var userXtremeId = GetUserXtremeIdiotsId(context);
        return userXtremeId == adminId;
    }

    /// <summary>
    /// Retrieves the user profile ID from the user's claims
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <returns>The user profile ID if found, otherwise null</returns>
    public static string? GetUserProfileId(AuthorizationHandlerContext context)
    {
        return context.User.FindFirst(UserProfileClaimType.UserProfileId)?.Value;
    }

    /// <summary>
    /// Determines if the current user is the owner of the specified resource
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="resourceUserProfileId">The resource owner's user profile ID</param>
    /// <returns>True if the user is the resource owner, false otherwise</returns>
    public static bool IsResourceOwner(AuthorizationHandlerContext context, Guid resourceUserProfileId)
    {
        var userProfileId = GetUserProfileId(context);
        return userProfileId is not null && userProfileId == resourceUserProfileId.ToString();
    }

    /// <summary>
    /// Checks if the user is authenticated
    /// </summary>
    /// <param name="context">The authorization context</param>
    /// <param name="requirement">The authorization requirement to succeed if access is granted</param>
    public static void CheckAuthenticated(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);
    }

    #endregion
}