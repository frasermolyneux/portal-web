using Microsoft.AspNetCore.Authorization;

namespace XtremeIdiots.Portal.Web.Auth.Requirements;

/// <summary>
/// Authorization requirement for accessing the global settings management interface
/// </summary>
public class AccessGlobalSettings : IAuthorizationRequirement
{
}
