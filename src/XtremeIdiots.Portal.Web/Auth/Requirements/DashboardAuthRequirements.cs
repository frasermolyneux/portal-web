using Microsoft.AspNetCore.Authorization;

namespace XtremeIdiots.Portal.Web.Auth.Requirements;

/// <summary>
/// Authorization requirement for accessing the admin dashboard
/// </summary>
public class AccessDashboard : IAuthorizationRequirement
{
}
