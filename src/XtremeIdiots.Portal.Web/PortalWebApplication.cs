using Azure.Identity;
using Azure.Monitor.Query;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MX.GeoLocation.Api.Client.V1;
using MX.InvisionCommunity.Api.Client;
using MX.Observability.ApplicationInsights.AspNetCore;
using System.Text.Json.Serialization;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Forums.Extensions;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Areas.Identity;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web;

public static class PortalWebApplication
{
    /// <summary>
    /// Stable, non-secret cache partition applied to every API client that opts into
    /// MX.Api.Client's L1 in-memory caching (currently GeoLocation and Invision;
    /// Servers ships no cache defaults so opt-in would be a no-op, and Repository is
    /// intentionally kept uncached — see the NOTE comments on each registration below).
    /// The partition is part of every cache key so that cache entries produced by this
    /// app are namespaced under a single, predictable prefix. It is not derived from
    /// configuration to keep the value deterministic across processes/instances and
    /// safe to log.
    /// </summary>
    internal const string ClientCachePartition = "portal-web";

    public static WebApplicationBuilder CreateBuilder(
        WebApplicationOptions options,
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configureBuilder?.Invoke(builder);

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
                    .Select("ApplicationInsights:*", environmentLabel)
                    .ConfigureRefresh(refresh =>
                        refresh.Register("Sentinel", environmentLabel, refreshAll: true)
                               .SetRefreshInterval(TimeSpan.FromMinutes(5)));

                options.ConfigureKeyVault(keyVault =>
                {
                    keyVault.SetCredential(credential);
                    keyVault.SetSecretRefreshInterval(TimeSpan.FromHours(1));
                });
            });
        }

        IdentityHostingStartup.ConfigureIdentityServices(builder.Services, builder.Configuration);
        builder.Services.AddScoped<IIdentityDatabaseInitializer, IdentityDatabaseInitializer>();

        builder.Services.AddAzureAppConfiguration();
        builder.Services.AddSingleton<ITelemetryInitializer, TelemetryInitializer>();
        builder.Services.AddLogging();

        builder.Services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            EnableAdaptiveSampling = false,
        });
        builder.Services.AddObservability();
        if (builder.Configuration.GetValue("ApplicationInsights:EnableProfiler", true))
        {
            builder.Services.AddServiceProfiler();
        }

        builder.Services.AddInvisionApiClient(clientOptions => clientOptions
            .WithBaseUrl(GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required"))
            .WithApiKeyAuthentication(GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:ApiKey", "XtremeIdiots:Forums:ApiKey configuration is required"), "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter)
            .WithCachePartition(ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));
        // NOTE (Invision client 1.0.63 / MX.Api.Client 2.3.77): the library ships curated
        // read-only cache defaults for ICoreApi.GetCoreHello (60s), ICoreApi.GetMember (30s)
        // and IDownloadsApi.GetDownloadFile (30s). IForumsApi (topic post/update) is
        // uncached by the library, so forum posting is unaffected. portal-web only READS
        // members (during OIDC sign-in in XtremeIdiotsAuth.cs) and never mutates a member
        // and re-reads it in the same request, so enabling defaults is read-after-write
        // safe. Health-check calls to GetCoreHello may return the last successful response
        // for up to 60s, which is an accepted trade-off for the shipped default.

        builder.Services.AddAdminActionTopics();
        builder.Services.AddScoped<IDemoManager, DemoManager>();

        builder.Services.AddSingleton(_ => new LogsQueryClient(new DefaultAzureCredential()));
        builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
        builder.Services.AddScoped<IAgentTelemetryService, AgentTelemetryService>();
        builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        builder.Services.AddScoped<INamespaceSettingsParser, NamespaceSettingsParser>();
        builder.Services.AddScoped<INamespaceSettingsSerializer, NamespaceSettingsSerializer>();
        builder.Services.AddScoped<IGlobalSettingsService, GlobalSettingsService>();
        builder.Services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        builder.Services.AddSingleton<IExternalTokenService, ExternalTokenService>();

        builder.Services.AddRepositoryApiClient(clientOptions => clientOptions
            .WithBaseUrl(GetConfigValue(builder.Configuration, "RepositoryApi:BaseUrl", "RepositoryApi:BaseUrl configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "RepositoryApi:ApplicationAudience", "RepositoryApi:ApplicationAudience configuration is required")));
        // NOTE (Repository client 4.2.22 / MX.Api.Client 2.3.77): client 4.2.22 fixed the
        // cross-sub-API cache-scoping crash at the source via a reflection-free
        // SharedCacheConfiguration, so opting into .WithCaching(c => c.UseLibraryDefaults())
        // would now build and run. We DELIBERATELY do NOT opt in. portal-web mutates game
        // servers (Create/Update/Delete/UpdateOrder) and maps (Create/Update/Delete/
        // UpdateImage/ClearImage/UpsertVote) in the same process; the client library's shipped
        // in-process L1 defaults (60s game-server reads, 10min single/collection map reads)
        // cannot see the Repository API's server-side Tiered tag invalidations, so enabling
        // them would produce read-after-write staleness. Per the task guardrail ("prefer
        // correctness over maximizing hit rate") portal-web keeps client L1 permanently off
        // and relies solely on the Repository API's server-side Tiered cache (game-server
        // single/list, dashboard, configuration single/collection, single-map) which
        // invalidates transparently on write. User profile/claims/auth surfaces and
        // mutations remain uncached end-to-end.

        builder.Services.AddServersApiClient(clientOptions => clientOptions
            .WithBaseUrl(GetConfigValue(builder.Configuration, "ServersIntegrationApi:BaseUrl", "ServersIntegrationApi:BaseUrl configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "ServersIntegrationApi:ApplicationAudience", "ServersIntegrationApi:ApplicationAudience configuration is required")));
        // NOTE (Servers client 4.1.14 / MX.Api.Client 2.3.77): the servers client ships
        // NO cache defaults (WithCaching(c => c.UseLibraryDefaults()) is a no-op for it)
        // so we deliberately do not enable client caching here. The bump to 4.1.14
        // provides crash-safe SharedCacheConfiguration scoping across sub-APIs alongside
        // GeoLocation/Invision, which now opt into caching above.

        if (!string.IsNullOrWhiteSpace(builder.Configuration["SyncApi:BaseUrl"]) &&
            !string.IsNullOrWhiteSpace(builder.Configuration["SyncApi:ApplicationAudience"]))
        {
            builder.Services.AddHttpClient<ISyncApiClient, SyncApiClient>();
        }
        else
        {
            builder.Services.AddSingleton<ISyncApiClient, NoOpSyncApiClient>();
        }

        builder.Services.AddGeoLocationApiClient(clientOptions => clientOptions
            .WithBaseUrl(GetConfigValue(builder.Configuration, "GeoLocationApi:BaseUrl", "GeoLocationApi:BaseUrl configuration is required"))
            .WithApiKeyAuthentication(GetConfigValue(builder.Configuration, "GeoLocationApi:ApiKey", "GeoLocationApi:ApiKey configuration is required"))
            .WithEntraIdAuthentication(GetConfigValue(builder.Configuration, "GeoLocationApi:ApplicationAudience", "GeoLocationApi:ApplicationAudience configuration is required"))
            .WithCachePartition(ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));
        // NOTE (GeoLocation client 1.2.98 / MX.Api.Client 2.3.77): the library ships
        // curated read-only cache defaults for IGeoLookupApi single-hostname lookups
        // (GetGeoLocation 60m; V1.1 GetCityGeoLocation 60m, GetInsightsGeoLocation 30m,
        // GetProxyCheck 15m, GetIpIntelligence 15m). Batch POST and DeleteMetadata are
        // NOT cached by the library. Enabling defaults here is unconditionally safe:
        // portal-web treats geolocation as pure read-only lookups and has no in-process
        // mutation-then-re-read flow for a hostname. Cache is L1 in-memory only.

        builder.Services.AddXtremeIdiotsAuth();
        builder.Services.AddAuthorization(authorizationOptions => authorizationOptions.AddXtremeIdiotsPolicies());

        builder.Services.AddCors(corsOptions =>
        {
            var corsBaseUrl = GetConfigValue(builder.Configuration, "XtremeIdiots:Forums:BaseUrl", "XtremeIdiots:Forums:BaseUrl configuration is required");
            if (!Uri.TryCreate(corsBaseUrl, UriKind.Absolute, out var corsUri))
                throw new InvalidOperationException($"XtremeIdiots:Forums:BaseUrl value '{corsBaseUrl}' is not a valid absolute URI for CORS origin configuration");
            var corsOrigin = corsUri.GetLeftPart(UriPartial.Authority);
            corsOptions.AddPolicy("CorsPolicy",
                policy => policy
                    .WithOrigins(corsOrigin)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });

        var mvcBuilder = builder.Services.AddControllersWithViews()
            .AddJsonOptions(jsonOptions =>
                jsonOptions.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

#if DEBUG
        mvcBuilder.AddRazorRuntimeCompilation();
#endif

        builder.Services.Configure<CookieTempDataProviderOptions>(cookieOptions => cookieOptions.Cookie.IsEssential = true);
        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();

        builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersOptions =>
        {
            forwardedHeadersOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            forwardedHeadersOptions.KnownNetworks.Clear();
            forwardedHeadersOptions.KnownProxies.Clear();
        });

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.UseForwardedHeaders();
        if (!string.IsNullOrWhiteSpace(app.Configuration["AzureAppConfiguration:Endpoint"]))
        {
            app.UseAzureAppConfiguration();
        }

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
        app.MapHealthChecks("/api/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        }).AllowAnonymous();
        app.MapHealthChecks("/api/health/ready").AllowAnonymous();
        app.MapInfoEndpoint();

        return app;
    }

    public async static Task InitializeAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var identityDatabaseInitializer = scope.ServiceProvider.GetRequiredService<IIdentityDatabaseInitializer>();
        await identityDatabaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetConfigValue(ConfigurationManager configuration, string key, string missingMessage)
    {
        return configuration[key]
            ?? configuration[$"XtremeIdiots.Portal.Web:{key}"]
            ?? throw new InvalidOperationException(missingMessage);
    }
}
