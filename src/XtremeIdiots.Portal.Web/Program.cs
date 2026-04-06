using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Primitives;
using MX.GeoLocation.Api.Client.V1;
using MX.InvisionCommunity.Api.Client;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Forums.Extensions;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web;
using XtremeIdiots.Portal.Web.Areas.Identity;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Azure App Configuration
var appConfigEndpoint = builder.Configuration["AzureAppConfiguration:Endpoint"];

if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
{
    var managedIdentityClientId = builder.Configuration["AzureAppConfiguration:ManagedIdentityClientId"];
    var environmentLabel = builder.Configuration["AzureAppConfiguration:Environment"] ?? builder.Environment.EnvironmentName;

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = managedIdentityClientId,
    });

    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        options.Connect(new Uri(appConfigEndpoint), credential)
            .Select("XtremeIdiots.Portal.Web:*", environmentLabel)
            .TrimKeyPrefix("XtremeIdiots.Portal.Web:")
            .Select("RepositoryApi:*", environmentLabel)
            .Select("ServersIntegrationApi:*", environmentLabel)
            .Select("SyncApi:*", environmentLabel)
            .Select("GeoLocationApi:*", environmentLabel)
            .Select("XtremeIdiots:*", environmentLabel)
            .Select("GameTracker:*", environmentLabel)
            .Select("Google:*", environmentLabel)
            .Select("FeatureManagement:*", environmentLabel)
            .ConfigureRefresh(refresh =>
                refresh.Register("Sentinel", environmentLabel, refreshAll: true)
                       .SetRefreshInterval(TimeSpan.FromMinutes(5)));

        options.ConfigureKeyVault(kv =>
        {
            kv.SetCredential(credential);
            kv.SetSecretRefreshInterval(TimeSpan.FromHours(1));
        });
    });
}

// Adaptive sampling settings
var samplingSettings = new SamplingPercentageEstimatorSettings
{
    InitialSamplingPercentage = double.TryParse(builder.Configuration["ApplicationInsights:InitialSamplingPercentage"], out var initPct) ? initPct : 5,
    MinSamplingPercentage = double.TryParse(builder.Configuration["ApplicationInsights:MinSamplingPercentage"], out var minPct) ? minPct : 5,
    MaxSamplingPercentage = double.TryParse(builder.Configuration["ApplicationInsights:MaxSamplingPercentage"], out var maxPct) ? maxPct : 60
};

// Identity services (must run after Azure App Configuration is loaded)
IdentityHostingStartup.ConfigureIdentityServices(builder.Services, builder.Configuration);

// Services
builder.Services.AddAzureAppConfiguration();

builder.Services.AddSingleton<ITelemetryInitializer, TelemetryInitializer>();
builder.Services.AddLogging();

builder.Services.Configure<TelemetryConfiguration>(telemetryConfiguration =>
{
    var telemetryProcessorChainBuilder = telemetryConfiguration.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
    telemetryProcessorChainBuilder.UseAdaptiveSampling(
        settings: samplingSettings,
        callback: null,
        excludedTypes: "Exception;Event");
    telemetryProcessorChainBuilder.Build();
});

builder.Services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
{
    EnableAdaptiveSampling = false,
});

builder.Services.AddServiceProfiler();

builder.Services.AddInvisionApiClient(options => options
    .WithBaseUrl(GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required"))
    .WithApiKeyAuthentication(GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:ApiKey", "XtremeIdiots:Forums:ApiKey configuration is required"), "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter));

builder.Services.AddAdminActionTopics();
builder.Services.AddScoped<IDemoManager, DemoManager>();

builder.Services.AddSingleton(_ => new LogsQueryClient(new DefaultAzureCredential()));
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<IAgentTelemetryService, AgentTelemetryService>();

builder.Services.AddRepositoryApiClient(options => options
    .WithBaseUrl(GetConfigValue(builder.Configuration, "RepositoryApi:BaseUrl", "RepositoryApi:BaseUrl configuration is required"))
    .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "RepositoryApi:ApplicationAudience", "RepositoryApi:ApplicationAudience configuration is required")));

builder.Services.AddServersApiClient(options => options
    .WithBaseUrl(GetConfigValue(builder.Configuration, "ServersIntegrationApi:BaseUrl", "ServersIntegrationApi:BaseUrl configuration is required"))
    .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "ServersIntegrationApi:ApplicationAudience", "ServersIntegrationApi:ApplicationAudience configuration is required")));

if (!string.IsNullOrWhiteSpace(builder.Configuration["SyncApi:BaseUrl"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["SyncApi:ApplicationAudience"]))
{
    builder.Services.AddHttpClient<ISyncApiClient, SyncApiClient>();
}
else
{
    builder.Services.AddSingleton<ISyncApiClient, NoOpSyncApiClient>();
}

builder.Services.AddGeoLocationApiClient(options => options
    .WithBaseUrl(GetConfigValue(builder.Configuration, "GeoLocationApi:BaseUrl", "GeoLocationApi:BaseUrl configuration is required"))
    .WithApiKeyAuthentication(GetConfigValue(builder.Configuration, "GeoLocationApi:ApiKey", "GeoLocationApi:ApiKey configuration is required"))
    .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "GeoLocationApi:ApplicationAudience", "GeoLocationApi:ApplicationAudience configuration is required")));

builder.Services.AddXtremeIdiotsAuth();
builder.Services.AddAuthorization(options => options.AddXtremeIdiotsPolicies());

builder.Services.AddCors(options =>
{
    var corsOrigin = GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required");
    options.AddPolicy("CorsPolicy",
        policy => policy
            .WithOrigins(corsOrigin)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Add MVC with conditional Razor runtime compilation
var mvcBuilder = builder.Services.AddControllersWithViews();

#if DEBUG
// Only add runtime compilation in Debug builds for development productivity
mvcBuilder.AddRazorRuntimeCompilation();
#endif

builder.Services.Configure<CookieTempDataProviderOptions>(options => options.Cookie.IsEssential = true);

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware pipeline
app.UseForwardedHeaders();
app.UseAzureAppConfiguration();

// Update adaptive sampling settings when configuration refreshes
ChangeToken.OnChange(
    app.Configuration.GetReloadToken,
    () =>
    {
        if (double.TryParse(app.Configuration["ApplicationInsights:MinSamplingPercentage"], out var min))
            samplingSettings.MinSamplingPercentage = min;
        if (double.TryParse(app.Configuration["ApplicationInsights:MaxSamplingPercentage"], out var max))
            samplingSettings.MaxSamplingPercentage = max;
    });

if (app.Environment.IsDevelopment())
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

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapInfoEndpoint();

app.UseHealthChecks(new PathString("/api/health"));

using (var scope = app.Services.CreateScope())
{
    var identityDataContext = scope.ServiceProvider.GetRequiredService<IdentityDataContext>();
    identityDataContext.Database.Migrate();
}

app.Run();

static string GetConfigValue(IConfiguration configuration, string key, string missingMessage)
{
    return configuration[key]
        ?? configuration[$"XtremeIdiots.Portal.Web:{key}"]
        ?? throw new InvalidOperationException(missingMessage);
}