using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestAuthenticationDefaults.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        try
        {
            var principal = TestPrincipalProfiles.Create(values.ToString());
            var ticket = new AuthenticationTicket(principal, TestAuthenticationDefaults.Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Task.FromResult(AuthenticateResult.Fail(exception));
        }
    }
}
