using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Security.Claims;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement(Attributes = "policy")]
public class PolicyTagHelper(IAuthorizationService authService, IHttpContextAccessor httpContextAccessor) : TagHelper
{
    private readonly IAuthorizationService authService = authService;
    private readonly ClaimsPrincipal principal = httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("HttpContext is not available");

    public required string Policy { get; set; }

    /// <summary>
    /// Optional resource to pass to the authorization handler for resource-based policy checks.
    /// When provided, enables handlers that require a resource (e.g., GameType) to evaluate correctly.
    /// </summary>
    public object? PolicyResource { get; set; }

    public async override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var result = PolicyResource != null
            ? await authService.AuthorizeAsync(principal, PolicyResource, Policy).ConfigureAwait(false)
            : await authService.AuthorizeAsync(principal, Policy).ConfigureAwait(false);

        if (!result.Succeeded)
            output.SuppressOutput();
    }
}