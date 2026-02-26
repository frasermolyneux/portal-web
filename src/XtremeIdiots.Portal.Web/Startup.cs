using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using MX.GeoLocation.Api.Client.V1;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using MX.InvisionCommunity.Api.Client;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Forums.Extensions;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

    private readonly SamplingPercentageEstimatorSettings samplingSettings = new()
    {
        InitialSamplingPercentage = double.TryParse(configuration["ApplicationInsights:InitialSamplingPercentage"], out var initPct) ? initPct : 5,
        MinSamplingPercentage = double.TryParse(configuration["ApplicationInsights:MinSamplingPercentage"], out var minPct) ? minPct : 5,
        MaxSamplingPercentage = double.TryParse(configuration["ApplicationInsights:MaxSamplingPercentage"], out var maxPct) ? maxPct : 60
    };

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAzureAppConfiguration();

        services.AddSingleton<ITelemetryInitializer, TelemetryInitializer>();
        services.AddLogging();

        services.Configure<TelemetryConfiguration>(telemetryConfiguration =>
        {
            var telemetryProcessorChainBuilder = telemetryConfiguration.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
            telemetryProcessorChainBuilder.UseAdaptiveSampling(
                settings: samplingSettings,
                callback: null,
                excludedTypes: "Exception");
            telemetryProcessorChainBuilder.Build();
        });

        services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            EnableAdaptiveSampling = false,
        });

        services.AddServiceProfiler();

        services.AddInvisionApiClient(options => options
            .WithBaseUrl(GetConfigValue("XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required"))
            .WithApiKeyAuthentication(GetConfigValue("XtremeIdiots:Forums:ApiKey", "XtremeIdiots:Forums:ApiKey configuration is required"), "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter));

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

        services.AddXtremeIdiotsAuth();
        services.AddAuthorization(options => options.AddXtremeIdiotsPolicies());

        services.AddCors(options =>
        {
            var corsOrigin = GetConfigValue("XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required");
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
        services.AddScoped<Services.IProxyCheckService, Services.ProxyCheckService>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddHealthChecks();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();
        app.UseAzureAppConfiguration();

        // Update adaptive sampling settings when configuration refreshes
        ChangeToken.OnChange(
            Configuration.GetReloadToken,
            () =>
            {
                if (double.TryParse(Configuration["ApplicationInsights:MinSamplingPercentage"], out var min))
                    samplingSettings.MinSamplingPercentage = min;
                if (double.TryParse(Configuration["ApplicationInsights:MaxSamplingPercentage"], out var max))
                    samplingSettings.MaxSamplingPercentage = max;
            });

        if (env.IsDevelopment())
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

        using var scope = app.ApplicationServices.CreateScope();
        var identityDataContext = scope.ServiceProvider.GetRequiredService<IdentityDataContext>();
        identityDataContext.Database.Migrate();
    }

    private string GetConfigValue(string key, string missingMessage)
    {
        return Configuration[key]
            ?? Configuration[$"XtremeIdiots.Portal.Web:{key}"]
            ?? throw new InvalidOperationException(missingMessage);
    }
}