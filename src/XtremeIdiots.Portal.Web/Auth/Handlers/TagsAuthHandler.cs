using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for tag definition operations (Tags.Read, Tags.Write).
/// </summary>
public class TagsAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case TagsRead:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Tags.Read");
                    break;
                case TagsWrite:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Tags.Write");
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
