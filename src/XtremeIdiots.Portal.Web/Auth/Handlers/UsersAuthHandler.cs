using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Web.Auth.Requirements;
using XtremeIdiots.Portal.Web.Extensions;

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
                    BaseAuthorizationHelper.CheckSeniorOrHeadAdminAccessWithResource(context, requirement);
                    break;
                case UsersLogOut:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.SeniorAndHeadAdminOnly);
                    break;
                case UsersArchive:
                    if (context.Resource is UserProfileDto profile &&
                        Guid.TryParse(context.User.UserProfileId(), out var actorProfileId) &&
                        actorProfileId != Guid.Empty &&
                        profile.UserProfileId != Guid.Empty &&
                        actorProfileId != profile.UserProfileId &&
                        !string.Equals(context.User.XtremeIdiotsId(), profile.XtremeIdiotsForumId, StringComparison.Ordinal))
                    {
                        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
                    }

                    break;
                case UsersManageNotificationPreferences:
                    BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
                    break;
                case UsersSearch:
                    BaseAuthorizationHelper.CheckClaimTypes(context, requirement, BaseAuthorizationHelper.ClaimGroups.AllAdminLevels);
                    break;
                case UsersActivityLog:
                    BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
