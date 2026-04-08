using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Handles authorization requirements for dashboard access.
/// The dashboard is read-only overview data, accessible to any admin role.
/// </summary>
public class DashboardAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is AccessDashboard)
            {
                BaseAuthorizationHelper.CheckClaimTypes(context, requirement,
                    BaseAuthorizationHelper.ClaimGroups.ServerAdminAccessLevels);
            }
        }

        return Task.CompletedTask;
    }
}
