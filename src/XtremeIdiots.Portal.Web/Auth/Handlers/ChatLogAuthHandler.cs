using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for chat log operations including read, server-specific read, and lock.
/// </summary>
public class ChatLogAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case ChatLogRead:
                    HandleChatLogRead(context, requirement);
                    break;
                case ChatLogReadServer:
                    HandleChatLogReadServer(context, requirement);
                    break;
                case ChatLogLock:
                    HandleChatLogLock(context, requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static void HandleChatLogRead(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "ChatLog.Read");
    }

    private static void HandleChatLogReadServer(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "ChatLog.ReadServer");
    }

    private static void HandleChatLogLock(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);

        if (context.Resource is GameType gameType)
            BaseAuthorizationHelper.CheckModeratorAccess(context, requirement, gameType);

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "ChatLog.Lock");
    }
}
