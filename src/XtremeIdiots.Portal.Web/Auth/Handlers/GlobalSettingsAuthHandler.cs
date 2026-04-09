using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for global settings operations — restricted to senior admins only
/// </summary>
public class GlobalSettingsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pendingRequirements = context.PendingRequirements;

        foreach (var requirement in pendingRequirements)
        {
            if (requirement is AccessGlobalSettings)
            {
                BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
            }
        }

        return Task.CompletedTask;
    }
}
