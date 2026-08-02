using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for player operations including read, delete, protected names, and tag assignment.
/// </summary>
public class PlayersAuthHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case PlayersRead:
                    if (context.Resource is GameType or PotentialAccessProbe)
                        BaseAuthorizationHelper.CheckSeniorOrMultipleGameAccessWithResource(context, requirement);
                    else
                        BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);

                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Players.Read");
                    break;
                case PlayersDelete:
                    BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Players.Delete");
                    break;
                case PlayersProtectedNamesWrite:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Players.ProtectedNames.Write");
                    break;
                case PlayersTagsWrite:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AdminLevelsExcludingModerators);
                    BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Players.Tags.Write");
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
