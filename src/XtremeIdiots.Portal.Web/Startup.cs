using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MX.GeoLocation.Api.Client.V1;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using XtremeIdiots.InvisionCommunity;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Forums.Extensions;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Helpers;
using XtremeIdiots.Portal.Web.UITest.Fakes;

namespace XtremeIdiots.Portal.Web;

public class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public IConfiguration Configuration { get; } = configuration;
    public IWebHostEnvironment Environment { get; } = environment;
    private bool IsUITestMode => UITestConfiguration.IsUITestMode(Configuration, Environment);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITelemetryInitializer, TelemetryInitializer>();
        services.AddLogging();

        services.Configure<TelemetryConfiguration>(telemetryConfiguration =>
        {
            var telemetryProcessorChainBuilder = telemetryConfiguration.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
            telemetryProcessorChainBuilder.UseAdaptiveSampling(
                settings: new SamplingPercentageEstimatorSettings
                {
                    InitialSamplingPercentage = 5,
                    MinSamplingPercentage = 5,
                    MaxSamplingPercentage = 60
                },
                callback: null,
                excludedTypes: "Exception");
            telemetryProcessorChainBuilder.Build();
        });

        services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            EnableAdaptiveSampling = false,
        });

        services.AddServiceProfiler();
        
        if (!IsUITestMode)
        {
            services.AddAzureAppConfiguration();
        }

        // Configure external services based on mode
        if (IsUITestMode)
        {
            ConfigureUITestServices(services);
        }
        else
        {
            ConfigureProductionServices(services);
        }

        services.AddXtremeIdiotsAuth();
        services.AddAuthorization(options => options.AddXtremeIdiotsPolicies());

        services.AddCors(options =>
        {
            var corsOrigin = IsUITestMode 
                ? "https://www.xtremeidiots.com" 
                : GetConfigValue("XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required");
            options.AddPolicy("CorsPolicy",
                builder => builder
                    .WithOrigins(corsOrigin)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });

        // Add MVC with conditional Razor runtime compilation
        var mvcBuilder = services.AddControllersWithViews();

#if DEBUG
        // Only add runtime compilation in Debug builds for development productivity
        mvcBuilder.AddRazorRuntimeCompilation();
#endif

        services.Configure<CookieTempDataProviderOptions>(options => options.Cookie.IsEssential = true);

        services.AddHttpClient();
        services.AddMemoryCache();
        
        // Register ProxyCheck service based on mode
        if (IsUITestMode)
        {
            services.AddScoped<Services.IProxyCheckService, Services.FakeProxyCheckService>();
        }
        else
        {
            services.AddScoped<Services.IProxyCheckService, Services.ProxyCheckService>();
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddHealthChecks();
        
        // Register UITest data seeder if in UITest mode
        if (IsUITestMode)
        {
            services.AddScoped<UITest.UITestDataSeeder>();
        }
    }

    private void ConfigureUITestServices(IServiceCollection services)
    {
        // Register fake implementations for UITest mode
        services.AddScoped<IAdminActionTopics, FakeAdminActionTopics>();
        services.AddScoped<IDemoManager, FakeDemoManager>();
        
        // For NuGet API clients, we still need to register them but with minimal configuration
        // They won't be called in UITest scenarios, but they need to exist for DI
        services.AddRepositoryApiClient(options => options
            .WithBaseUrl("http://localhost:5000") // Dummy URL
            .WithEntraIdAuthentication("api://uitest")); // Dummy audience
        
        services.AddServersApiClient(options => options
            .WithBaseUrl("http://localhost:5001") // Dummy URL
            .WithEntraIdAuthentication("api://uitest")); // Dummy audience
        
        services.AddGeoLocationApiClient(options => options
            .WithBaseUrl("http://localhost:5002") // Dummy URL
            .WithApiKeyAuthentication("uitest-key") // Dummy key
            .WithEntraIdAuthentication("api://uitest")); // Dummy audience
    }

    private void ConfigureProductionServices(IServiceCollection services)
    {
        services.AddInvisionApiClient(options =>
        {
            options.BaseUrl = GetConfigValue("XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required");
            options.ApiKey = GetConfigValue("XtremeIdiots:Forums:ApiKey", "XtremeIdiots:Forums:ApiKey configuration is required");
        });

        services.AddAdminActionTopics();
        services.AddScoped<IDemoManager, DemoManager>();

        services.AddRepositoryApiClient(options => options
            .WithBaseUrl(GetConfigValue("RepositoryApi:BaseUrl", "RepositoryApi:BaseUrl configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue("RepositoryApi:ApplicationAudience", "RepositoryApi:ApplicationAudience configuration is required")));

        services.AddServersApiClient(options => options
            .WithBaseUrl(GetConfigValue("ServersIntegrationApi:BaseUrl", "ServersIntegrationApi:BaseUrl configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue("ServersIntegrationApi:ApplicationAudience", "ServersIntegrationApi:ApplicationAudience configuration is required")));

        services.AddGeoLocationApiClient(options => options
            .WithBaseUrl(GetConfigValue("GeoLocationApi:BaseUrl", "GeoLocationApi:BaseUrl configuration is required"))
            .WithApiKeyAuthentication(GetConfigValue("GeoLocationApi:ApiKey", "GeoLocationApi:ApiKey configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue("GeoLocationApi:ApplicationAudience", "GeoLocationApi:ApplicationAudience configuration is required")));
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();
        
        if (!IsUITestMode)
        {
            app.UseAzureAppConfiguration();
        }

        if (env.IsDevelopment() || IsUITestMode)
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Errors/Display/500");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseCookiePolicy();
        app.UseRouting();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseStatusCodePagesWithRedirects("/Errors/Display/{0}");

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();

            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
        });

        app.UseHealthChecks(new PathString("/api/health"));

        // Initialize database and seed data
        using var scope = app.ApplicationServices.CreateScope();
        var identityDataContext = scope.ServiceProvider.GetRequiredService<IdentityDataContext>();
        
        if (IsUITestMode)
        {
            // For UITest mode, ensure database is created and seed test data
            identityDataContext.Database.EnsureCreated();
            
            var seeder = scope.ServiceProvider.GetRequiredService<UITest.UITestDataSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }
        else
        {
            // For production/dev, run migrations
            identityDataContext.Database.Migrate();
        }
    }

    private string GetConfigValue(string key, string missingMessage)
    {
        // In UITest mode, return dummy values if config is missing
        if (IsUITestMode)
        {
            return Configuration[key] 
                ?? Configuration[$"XtremeIdiots.Portal.Web:{key}"]
                ?? "uitest-value";
        }
        
        return Configuration[key]
            ?? Configuration[$"XtremeIdiots.Portal.Web:{key}"]
            ?? throw new InvalidOperationException(missingMessage);
    }
}