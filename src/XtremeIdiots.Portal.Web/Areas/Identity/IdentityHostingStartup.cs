using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

using XtremeIdiots.Portal.Web.Areas.Identity.Data;

namespace XtremeIdiots.Portal.Web.Areas.Identity;

public static class IdentityHostingStartup
{
    private const string AuthClientIdKey = "XtremeIdiots:Auth:ClientId";
    private const string AuthClientSecretKey = "XtremeIdiots:Auth:ClientSecret";
    private const string AppConfigNamespacePrefix = "XtremeIdiots.Portal.Web:";
    private const string SqlConnectionStringKey = "sql_connection_string";

    private const int SecurityStampValidationIntervalMinutes = 15;
    private const int CookieExpirationDays = 7;
    private const string ApplicationName = "portal";
    private const string CookieName = "XIPortal";
    private const string OAuthSchemeName = "XtremeIdiots";
    public const string TestOAuthSchemeName = "XtremeIdiotsTest";

    public static void ConfigureIdentityServices(IServiceCollection services, IConfiguration configuration)
    {
        ValidateConfiguration(configuration);
        ConfigureDatabase(services, configuration);
        ConfigureIdentity(services);
        ConfigureCookiePolicy(services);
        ConfigureAuthentication(services, configuration);
        ConfigureDataProtection(services);
    }

    private static void ValidateConfiguration(IConfiguration configuration)
    {
        string[] requiredKeys =
        [
            AuthClientIdKey,
            AuthClientSecretKey,
            SqlConnectionStringKey
        ];

        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrEmpty(GetConfigurationValue(configuration, key)))
            {
                throw new InvalidOperationException($"Required configuration key '{key}' is missing or empty");
            }
        }
    }

    private static void ConfigureDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDataContext>(options =>
            options.UseSqlServer(GetConfigurationValue(configuration, SqlConnectionStringKey)));
    }

    private static void ConfigureIdentity(IServiceCollection services)
    {
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = string.Empty;
        }).AddDefaultTokenProviders()
        .AddEntityFrameworkStores<IdentityDataContext>();

        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(SecurityStampValidationIntervalMinutes));
    }

    private static void ConfigureCookiePolicy(IServiceCollection services)
    {
        services.Configure<CookiePolicyOptions>(options =>
        {
            options.CheckConsentNeeded = context => true;
            options.MinimumSameSitePolicy = SameSiteMode.None;
            options.Secure = CookieSecurePolicy.Always;
        });
    }

    private static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        // Do not override Identity's default schemes — AddIdentity already sets:
        //   DefaultAuthenticateScheme = Identity.Application
        //   DefaultSignInScheme       = Identity.External
        //   DefaultChallengeScheme    = Identity.Application (login page redirect)
        // The controller explicitly challenges "XtremeIdiots" when needed.
        var authentication = services.AddAuthentication();
        authentication.AddOAuth(OAuthSchemeName, options =>
        {
            // Sign into the external cookie so SignInManager.GetExternalLoginInfoAsync() works
            options.SignInScheme = IdentityConstants.ExternalScheme;

            // Must be essential so cookie consent policy doesn't block the correlation cookie
            options.CorrelationCookie.IsEssential = true;

            options.ClientId = GetConfigurationValue(configuration, AuthClientIdKey) ?? throw new InvalidOperationException("OAuth client ID is required");
            options.ClientSecret = GetConfigurationValue(configuration, AuthClientSecretKey) ?? throw new InvalidOperationException("OAuth client secret is required");

            options.CallbackPath = new PathString("/signin-xtremeidiots");

            var forumSiteUrl = (configuration["XtremeIdiots:Forums:BaseUrl"] ?? "https://www.xtremeidiots.com").TrimEnd('/');
            options.AuthorizationEndpoint = configuration["xtremeidiots_auth_authorization_endpoint"] ?? $"{forumSiteUrl}/oauth/authorize/";
            options.TokenEndpoint = configuration["xtremeidiots_auth_token_endpoint"] ?? $"{forumSiteUrl}/oauth/token/";
            options.UserInformationEndpoint = configuration["xtremeidiots_auth_userinfo_endpoint"] ?? $"{forumSiteUrl}/api/core/me";

            ConfigureOAuthClaimsAndEvents(options);
        });

        var testClientId = GetConfigurationValue(configuration, "XtremeIdiots:TestAuth:ClientId");
        var testClientSecret = GetConfigurationValue(configuration, "XtremeIdiots:TestAuth:ClientSecret");
        if (!string.IsNullOrWhiteSpace(testClientId) || !string.IsNullOrWhiteSpace(testClientSecret))
        {
            if (string.IsNullOrWhiteSpace(testClientId) || string.IsNullOrWhiteSpace(testClientSecret))
                throw new InvalidOperationException("Both test OAuth client ID and secret are required.");

            authentication.AddOAuth(TestOAuthSchemeName, options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.CorrelationCookie.IsEssential = true;
                options.ClientId = testClientId;
                options.ClientSecret = testClientSecret;
                options.CallbackPath = new PathString("/signin-xtremeidiots-test");
                options.AuthorizationEndpoint = "https://test.xtremeidiots.com/oauth/authorize/";
                options.TokenEndpoint = "https://test.xtremeidiots.com/oauth/token/";
                options.UserInformationEndpoint = "https://test.xtremeidiots.com/api/core/me";
                ConfigureOAuthClaimsAndEvents(options);
            });
        }

        // Configure Identity's application cookie (replaces the removed AddCookie call)
        services.ConfigureApplicationCookie(options =>
        {
            options.AccessDeniedPath = "/Errors/Display/401";
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(CookieExpirationDays);
            options.LoginPath = "/Identity/Login";
            options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
            options.SlidingExpiration = true;
            var redirectToLogin = options.Events.OnRedirectToLogin;
            var redirectToDenied = options.Events.OnRedirectToAccessDenied;
            options.Events.OnRedirectToLogin = context =>
                ApiStatusOrRedirect(context, StatusCodes.Status401Unauthorized, redirectToLogin);
            options.Events.OnRedirectToAccessDenied = context =>
                ApiStatusOrRedirect(context, StatusCodes.Status403Forbidden, redirectToDenied);
        });

        // Ensure the external cookie used during OAuth callback is not blocked by consent policy
        services.ConfigureExternalCookie(options => options.Cookie.IsEssential = true);
    }

    private static Task ApiStatusOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int status,
        Func<RedirectContext<CookieAuthenticationOptions>, Task> redirect)
    {
        if (!context.Request.Path.StartsWithSegments("/api/integration/v1"))
            return redirect(context);
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }

    private static void ConfigureOAuthClaimsAndEvents(OAuthOptions options)
    {
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");

        options.Scope.Add("profile");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    var contentAsString = await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
                    var user = JsonDocument.Parse(contentAsString);

                    context.RunClaimActions(user.RootElement);
                    if (context.Scheme.Name == TestOAuthSchemeName)
                    {
                        var identity = context.Identity
                            ?? throw new InvalidOperationException("The test provider did not return an identity.");
                        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)
                            ?? throw new InvalidOperationException("The test provider did not return a subject.");
                        identity.RemoveClaim(subject);
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, TestSubjectKey(context.Options.ClientId, subject.Value)));
                    }
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException("Failed to retrieve user information from OAuth provider", ex);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("Failed to parse user information from OAuth provider", ex);
                }
            }
        };
    }

    internal static string TestSubjectKey(string clientId, string subject)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("A test OAuth client ID and subject are required.");
        var input = JsonSerializer.SerializeToUtf8Bytes(new[] { "https://test.xtremeidiots.com", clientId, subject });
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void ConfigureDataProtection(IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<IdentityDataContext>();
    }

    private static string? GetConfigurationValue(IConfiguration configuration, string key)
    {
        return configuration[key] ?? configuration[$"{AppConfigNamespacePrefix}{key}"];
    }
}
