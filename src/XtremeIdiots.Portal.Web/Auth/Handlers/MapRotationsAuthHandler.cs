using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

public class MapRotationsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case MapRotationsRead:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "MapRotations.Read");
                    break;
                case MapRotationsWrite:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "MapRotations.Write");
                    break;
                case MapRotationsDeploy:
                    BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "MapRotations.Deploy");
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
