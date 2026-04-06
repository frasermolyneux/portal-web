using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

public class MapRotationsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pendingRequirements = context.PendingRequirements;

        foreach (var requirement in pendingRequirements)
        {
            switch (requirement)
            {
                case AccessMapRotations:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
                    break;
                case ManageMapRotations:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    break;
                case CreateMapRotation:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    break;
                case EditMapRotation:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    break;
                case DeleteMapRotation:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
