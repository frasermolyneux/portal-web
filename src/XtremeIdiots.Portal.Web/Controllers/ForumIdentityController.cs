using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Areas.Identity;

namespace XtremeIdiots.Portal.Web.Controllers;

public sealed class ForumIdentityController(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    IAuthenticationSchemeProvider schemes,
    IRepositoryApiClient repository) : Controller
{
    private const string ProductionScheme = "XtremeIdiots";

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginTest(string? returnUrl)
    {
        if (await schemes.GetSchemeAsync(IdentityHostingStartup.TestOAuthSchemeName).ConfigureAwait(false) is null)
            return NotFound();
        var callback = Url.Action("ExternalLoginCallback", "Identity", new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(IdentityHostingStartup.TestOAuthSchemeName, callback);
        return Challenge(properties, IdentityHostingStartup.TestOAuthSchemeName);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkTest()
    {
        if (await schemes.GetSchemeAsync(IdentityHostingStartup.TestOAuthSchemeName).ConfigureAwait(false) is null)
            return NotFound();
        var user = await userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return Unauthorized();
        var callback = Url.Action(nameof(ConfirmProduction));
        return Challenge(signInManager.ConfigureExternalAuthenticationProperties(ProductionScheme, callback, user.Id), ProductionScheme);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ConfirmProduction(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return Unauthorized();
        var info = await signInManager.GetExternalLoginInfoAsync(user.Id).ConfigureAwait(false);
        if (info is null || info.LoginProvider != ProductionScheme || info.ProviderKey != user.Id)
            return BadRequest("Production account verification failed.");
        var owner = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);
        if (owner?.Id != user.Id)
            return Forbid();
        var profile = await repository.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(user.Id, cancellationToken).ConfigureAwait(false);
        if (!profile.IsSuccess || profile.Result?.Data is not { ArchivedAtUtc: null })
            return Forbid();

        var callback = Url.Action(nameof(ConfirmTest));
        return Challenge(signInManager.ConfigureExternalAuthenticationProperties(IdentityHostingStartup.TestOAuthSchemeName, callback, user.Id), IdentityHostingStartup.TestOAuthSchemeName);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ConfirmTest(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return Unauthorized();
        var info = await signInManager.GetExternalLoginInfoAsync(user.Id).ConfigureAwait(false);
        if (info is null || info.LoginProvider != IdentityHostingStartup.TestOAuthSchemeName)
            return BadRequest("Test account verification failed.");
        var profile = await repository.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(user.Id, cancellationToken).ConfigureAwait(false);
        if (!profile.IsSuccess || profile.Result?.Data is not { ArchivedAtUtc: null })
            return Forbid();
        var existing = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);
        if (existing is not null)
            return existing.Id == user.Id ? RedirectToAction("Manage", "Profile") : Conflict("This test account is already linked.");
        var result = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);
        return result.Succeeded
            ? RedirectToAction("Manage", "Profile")
            : Conflict("The test account could not be linked.");
    }
}
