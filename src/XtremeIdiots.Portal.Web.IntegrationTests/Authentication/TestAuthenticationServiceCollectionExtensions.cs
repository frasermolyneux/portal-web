using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

internal static class TestAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddPortalTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthenticationDefaults.Scheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultForbidScheme = TestAuthenticationDefaults.Scheme;
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationDefaults.Scheme, _ => { });

        return services;
    }
}
