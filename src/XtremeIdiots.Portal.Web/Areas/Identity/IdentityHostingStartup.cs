using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

using XtremeIdiots.Portal.Web.Areas.Identity;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.Helpers;

[assembly: HostingStartup(typeof(IdentityHostingStartup))]
namespace XtremeIdiots.Portal.Web.Areas.Identity;

public class IdentityHostingStartup : IHostingStartup
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
    private const string UITestSchemeName = "UITest";

    // Static connection for UITest SQLite in-memory database to keep it alive
    private static SqliteConnection? _uiTestConnection;

    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices((context, services) =>
        {
            var isUITest = UITestConfiguration.IsUITestMode(context.Configuration);
            
            if (!isUITest)
            {
                ValidateConfiguration(context.Configuration);
            }
            
            ConfigureDatabase(services, context.Configuration, isUITest);
            ConfigureIdentity(services);
            ConfigureCookiePolicy(services);
            ConfigureAuthentication(services, context.Configuration, isUITest);
            ConfigureDataProtection(services);
        });
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

    private static void ConfigureDatabase(IServiceCollection services, IConfiguration configuration, bool isUITest)
    {
        if (isUITest)
        {
            // Use SQLite in-memory database for UITest mode
            _uiTestConnection = new SqliteConnection("Data Source=:memory:");
            _uiTestConnection.Open();
            
            services.AddDbContext<IdentityDataContext>(options =>
                options.UseSqlite(_uiTestConnection));
        }
        else
        {
            services.AddDbContext<IdentityDataContext>(options =>
                options.UseSqlServer(GetConfigurationValue(configuration, SqlConnectionStringKey)));
        }
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

    private static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration, bool isUITest)
    {
        if (isUITest)
        {
            // Configure test authentication for UITest mode
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = UITestSchemeName;
            })
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/Errors/Display/401";
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(CookieExpirationDays);
                options.LoginPath = "/Identity/Login";
                options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
                options.SlidingExpiration = true;
            })
            .AddScheme<AuthenticationSchemeOptions, UITestAuthenticationHandler>(UITestSchemeName, options => { });
        }
        else
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OAuthSchemeName;
            })
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/Errors/Display/401";
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(CookieExpirationDays);
                options.LoginPath = "/Identity/Login";
                options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
                options.SlidingExpiration = true;
            })
            .AddOAuth(OAuthSchemeName, options =>
            {
                options.ClientId = GetConfigurationValue(configuration, AuthClientIdKey) ?? throw new InvalidOperationException("OAuth client ID is required");
                options.ClientSecret = GetConfigurationValue(configuration, AuthClientSecretKey) ?? throw new InvalidOperationException("OAuth client secret is required");

                options.CallbackPath = new PathString("/signin-xtremeidiots");

                options.AuthorizationEndpoint = configuration["xtremeidiots_auth_authorization_endpoint"] ?? "https://www.xtremeidiots.com/oauth/authorize/";
                options.TokenEndpoint = configuration["xtremeidiots_auth_token_endpoint"] ?? "https://www.xtremeidiots.com/oauth/token/";
                options.UserInformationEndpoint = configuration["xtremeidiots_auth_userinfo_endpoint"] ?? "https://www.xtremeidiots.com/api/core/me";

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
            });
        }
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