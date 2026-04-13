using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

public class MapsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is MapsRead)
            {
                BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
                BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Maps.Read");
            }
        }

        return Task.CompletedTask;
    }
}