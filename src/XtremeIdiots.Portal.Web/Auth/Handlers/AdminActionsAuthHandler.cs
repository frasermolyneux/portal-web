using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Handles authorization for all AdminActions.* policies including create, edit, delete, claim, lift, and reassign.
/// </summary>
public class AdminActionsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case AdminActionsRead:
                    HandleRead(context, requirement);
                    break;
                case AdminActionsCreate:
                    HandleCreate(context, requirement);
                    break;
                case AdminActionsEdit:
                    HandleEdit(context, requirement);
                    break;
                case AdminActionsDelete:
                    HandleDelete(context, requirement);
                    break;
                case AdminActionsClaim:
                    HandleClaim(context, requirement);
                    break;
                case AdminActionsLift:
                    HandleLift(context, requirement);
                    break;
                case AdminActionsReassign:
                    HandleReassign(context, requirement);
                    break;
                case AdminActionsCreateTopic:
                    HandleCreateTopic(context, requirement);
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }

    #region Authorization Handlers

    private static void HandleRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Read");
    }

    private static void HandleCreate(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, AdminActionType> refTuple)
        {
            BaseAuthorizationHelper.CheckGameAdminAccess(context, requirement, refTuple.Item1);
            if (IsModeratorLevelAction(refTuple.Item2))
                BaseAuthorizationHelper.CheckModeratorAccess(context, requirement, refTuple.Item1);
        }
        else if (context.Resource is (GameType gameType, AdminActionType adminActionType))
        {
            BaseAuthorizationHelper.CheckGameAdminAccess(context, requirement, gameType);
            if (IsModeratorLevelAction(adminActionType))
                BaseAuthorizationHelper.CheckModeratorAccess(context, requirement, gameType);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Create");
    }

    private static void HandleEdit(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, AdminActionType, string?> refTuple)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, refTuple.Item1);
            CheckActionSpecificEditPermissions(context, requirement, refTuple.Item1, refTuple.Item2, refTuple.Item3);
        }
        else if (context.Resource is (GameType gameType, AdminActionType adminActionType, string adminIdValue))
        {
            var adminId = string.IsNullOrWhiteSpace(adminIdValue) ? null : adminIdValue;
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
            CheckActionSpecificEditPermissions(context, requirement, gameType, adminActionType, adminId);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Edit");
    }

    private static void HandleDelete(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Delete");
    }

    private static void HandleClaim(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Claim");
    }

    private static void HandleLift(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, string> refTuple)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, refTuple.Item1);
            if (context.User.HasClaim(UserProfileClaimType.GameAdmin, refTuple.Item1.ToString()) &&
                BaseAuthorizationHelper.IsActionOwner(context, refTuple.Item2))
                context.Succeed(requirement);
        }
        else if (context.Resource is (GameType gameType, string adminId))
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
            if (context.User.HasClaim(UserProfileClaimType.GameAdmin, gameType.ToString()) &&
                BaseAuthorizationHelper.IsActionOwner(context, adminId))
                context.Succeed(requirement);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Lift");
    }

    private static void HandleReassign(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is GameType gameType)
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.Reassign");
    }

    private static void HandleCreateTopic(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "AdminActions.CreateTopic");
    }

    #endregion

    #region Helper Methods

    private static bool IsModeratorLevelAction(AdminActionType adminActionType)
    {
        return adminActionType is AdminActionType.Observation or
                               AdminActionType.Warning or
                               AdminActionType.Kick;
    }

    private static void CheckActionSpecificEditPermissions(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, GameType gameType, AdminActionType adminActionType, string? adminId)
    {
        var gameTypeString = gameType.ToString();
        var isOwner = BaseAuthorizationHelper.IsActionOwner(context, adminId);
        var isModerator = context.User.HasClaim(UserProfileClaimType.Moderator, gameTypeString);
        var isGameAdmin = context.User.HasClaim(UserProfileClaimType.GameAdmin, gameTypeString);

        switch (adminActionType)
        {
            case AdminActionType.Observation:
            case AdminActionType.Warning:
            case AdminActionType.Kick:
                if ((isModerator || isGameAdmin) && isOwner)
                    context.Succeed(requirement);
                break;

            case AdminActionType.TempBan:
            case AdminActionType.Ban:
                if (isGameAdmin && isOwner)
                    context.Succeed(requirement);
                break;

            default:
                break;
        }
    }

    #endregion
}