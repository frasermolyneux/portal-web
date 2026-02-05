using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Areas.Identity;

/// <summary>
/// Authentication handler for UITest mode that automatically signs in a test user
/// </summary>
public class UITestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public UITestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
        : base(options, logger, encoder)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if user is already signed in
        if (Context.User.Identity?.IsAuthenticated == true)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name));
        }

        // Find or create the test user
        var testUser = await _userManager.FindByNameAsync("uitest@xtremeidiots.com");
        if (testUser is null)
        {
            Logger.LogWarning("UITest user not found. Authentication will fail until user is seeded.");
            return AuthenticateResult.NoResult();
        }

        // Sign in the test user
        await _signInManager.SignInAsync(testUser, isPersistent: true);

        // Create claims principal
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, testUser.Id),
            new(ClaimTypes.Name, testUser.UserName ?? "UITest User"),
            new(ClaimTypes.Email, testUser.Email ?? "uitest@xtremeidiots.com")
        };

        // Add user claims
        var userClaims = await _userManager.GetClaimsAsync(testUser);
        claims.AddRange(userClaims);

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
