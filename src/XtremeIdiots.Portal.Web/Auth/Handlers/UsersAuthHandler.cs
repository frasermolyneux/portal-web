using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for user management operations — none are assignable as additional permissions.
/// </summary>
public class UsersAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case UsersRead:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.SeniorAndHeadAdminOnly);
                    break;
                case UsersManageClaims:
                    HandleManageClaims(context, requirement);
                    break;
                case UsersSearch:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
                    break;
                case UsersActivityLog:
                    BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static void HandleManageClaims(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is GameType gameType)
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
    }
}