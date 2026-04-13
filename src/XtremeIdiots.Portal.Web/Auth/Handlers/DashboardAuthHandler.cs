using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Handles authorization requirements for dashboard access.
/// </summary>
public class DashboardAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is DashboardRead)
            {
                BaseAuthorizationHelper.CheckClaimTypes(context, requirement,
                    BaseAuthorizationHelper.ClaimGroups.ServerAdminAccessLevels);
                BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Dashboard.Read");
            }
        }

        return Task.CompletedTask;
    }
}
