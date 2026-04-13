using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Auth.Handlers;

/// <summary>
/// Authorization handler for demo operations including read, write, and delete.
/// </summary>
public class DemosAuthHandler(IHttpContextAccessor httpContextAccessor) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            switch (requirement)
            {
                case DemosRead:
                    HandleDemosReadWrite(context, requirement, "Demos.Read");
                    break;
                case DemosWrite:
                    HandleDemosReadWrite(context, requirement, "Demos.Write");
                    break;
                case DemosDelete:
                    HandleDemosDelete(context, requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private void HandleDemosReadWrite(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, string permissionClaimType)
    {
        BaseAuthorizationHelper.CheckAuthenticated(context, requirement);

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Request.Headers.ContainsKey("demo-manager-auth-key") == true)
            context.Succeed(requirement);

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, permissionClaimType);
    }

    private static void HandleDemosDelete(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
    {
        BaseAuthorizationHelper.CheckSeniorAdminAccess(context, requirement);

        if (context.Resource is Tuple<GameType, Guid> refTuple)
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, refTuple.Item1);
            if (BaseAuthorizationHelper.IsResourceOwner(context, refTuple.Item2))
                context.Succeed(requirement);
        }
        else if (context.Resource is (GameType gameType, Guid userProfileId))
        {
            BaseAuthorizationHelper.CheckHeadAdminAccess(context, requirement, gameType);
            if (BaseAuthorizationHelper.IsResourceOwner(context, userProfileId))
                context.Succeed(requirement);
        }

        BaseAuthorizationHelper.CheckDirectPermissionGrant(context, requirement, "Demos.Delete");
    }
}