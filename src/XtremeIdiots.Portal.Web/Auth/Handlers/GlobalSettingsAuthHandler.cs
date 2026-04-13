using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for global settings — restricted to senior admins only, not assignable.
/// </summary>
public class GlobalSettingsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is GlobalSettingsAdmin)
            {
                BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
            }
        }

        return Task.CompletedTask;
    }
}
