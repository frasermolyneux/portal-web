using Microsoft.Extensions.DependencyInjection;
using MX.Api.Client.Caching;
using MX.GeoLocation.Api.Client.V1;
using MX.InvisionCommunity.Api.Abstractions;
using MX.InvisionCommunity.Api.Client;
using System.Reflection;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Tests.Composition;

/// <summary>
/// Host-boot smoke tests that mirror <see cref="PortalWebApplication"/>'s API client
/// registrations and assert DI composition succeeds under the same shape production runs.
///
/// This is a "never again" regression guard: the original portal-web production crash
/// was a cross-sub-API cache-scoping failure inside the client library that manifested
/// at DI-composition time. These tests build the same registration graph (with caching
/// enabled where <see cref="PortalWebApplication"/> enables it), call
/// <see cref="ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(IServiceCollection)"/>,
/// and resolve every typed sub-API surface portal-web actually uses. If any future
/// client / MX.Api.Client release reintroduces a composition-time crash, these tests
/// fail before the app boots.
/// </summary>
public sealed class HostBootCompositionTests
{
    [Fact]
    public void RepositoryApiClient_ResolvesAndExposesAllSubApisUsedByPortalWeb()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Same fluent registration shape as PortalWebApplication.cs.
        // Client L1 caching is DELIBERATELY not enabled (see the NOTE comment
        // in PortalWebApplication.cs) — portal-web relies on server-side
        // Tiered caching for read-after-write correctness.
        services.AddRepositoryApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://repository.test.invalid")
            .WithEntraIdAuthentication("api://repository-test"));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IRepositoryApiClient>();
        Assert.NotNull(client);

        // Assert every sub-API surface portal-web injects at runtime is
        // reachable through the composed client. This mirrors the property-
        // access pattern used across the ApiControllers/ Controllers folders.
        Assert.NotNull(client.AdminActions.V1);
        Assert.NotNull(client.BanFileMonitors.V1);
        Assert.NotNull(client.CentralBanFileStatus.V1);
        Assert.NotNull(client.ChatMessages.V1);
        Assert.NotNull(client.ConnectedPlayers.V1);
        Assert.NotNull(client.Dashboard.V1);
        Assert.NotNull(client.DashboardAnalytics.V1);
        Assert.NotNull(client.DataMaintenance.V1);
        Assert.NotNull(client.Demos.V1);
        Assert.NotNull(client.GameAnalytics.V1);
        Assert.NotNull(client.GameServerConfigurations.V1);
        Assert.NotNull(client.GameServers.V1);
        Assert.NotNull(client.GameServersEvents.V1);
        Assert.NotNull(client.GameServersStats.V1);
        Assert.NotNull(client.GameTrackerBanner.V1);
        Assert.NotNull(client.GlobalAnalytics.V1);
        Assert.NotNull(client.GlobalConfigurations.V1);
        Assert.NotNull(client.LiveStatus.V1);
        Assert.NotNull(client.MapAnalytics.V1);
        Assert.NotNull(client.MapRotations.V1);
        Assert.NotNull(client.Maps.V1);
        Assert.NotNull(client.NotificationPreferences.V1);
        Assert.NotNull(client.Notifications.V1);
        Assert.NotNull(client.NotificationTypes.V1);
        Assert.NotNull(client.Players.V1);
        Assert.NotNull(client.RecentPlayers.V1);
        Assert.NotNull(client.ServerAnalytics.V1);
        Assert.NotNull(client.Tags.V1);
        Assert.NotNull(client.UserProfiles.V1);
    }

    [Fact]
    public void RepositoryApiClient_ResolvesInSeparateScopesWithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRepositoryApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://repository.test.invalid")
            .WithEntraIdAuthentication("api://repository-test"));

        using var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
        {
            var first = scope.ServiceProvider.GetRequiredService<IRepositoryApiClient>();
            Assert.NotNull(first);
        }

        using (var scope = provider.CreateScope())
        {
            var second = scope.ServiceProvider.GetRequiredService<IRepositoryApiClient>();
            Assert.NotNull(second);
        }
    }

    [Fact]
    public void GeoLocationApiClient_ResolvesWithLibraryCacheDefaults_AndExposesV1AndV11Surfaces()
    {
        // Same fluent registration shape as PortalWebApplication.cs (including the
        // caching opt-in), so any composition-time cache-scoping regression surfaces
        // here instead of at production boot.
        var services = BuildBaseServices();
        services.AddGeoLocationApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://geolocation.test.invalid")
            .WithApiKeyAuthentication("test-key")
            .WithEntraIdAuthentication("api://geolocation-test")
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IGeoLocationApiClient>();
        Assert.NotNull(client);
        Assert.NotNull(client.GeoLookup.V1);
        Assert.NotNull(client.GeoLookup.V1_1);
        Assert.NotNull(client.ApiHealth);
        Assert.NotNull(client.ApiInfo);

        // Both V1 and V1.1 typed sub-APIs should be resolvable directly too — this is
        // how controllers/services inject them (see e.g. HomeController /
        // IpIntelligenceService).
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MX.GeoLocation.Abstractions.Interfaces.V1.IGeoLookupApi>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MX.GeoLocation.Abstractions.Interfaces.V1_1.IGeoLookupApi>());
    }

    [Fact]
    public void InvisionApiClient_ResolvesWithLibraryCacheDefaults_AndExposesCoreDownloadsAndForums()
    {
        var services = BuildBaseServices();
        services.AddInvisionApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://forums.test.invalid")
            .WithApiKeyAuthentication("test-key", "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter)
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IInvisionApiClient>();
        Assert.NotNull(client);
        // Core / Downloads / Forums are the only three typed sub-APIs the library
        // registers; the first two participate in library cache defaults, Forums
        // (topic post/update) is uncached and stays unaffected by mutations.
        Assert.NotNull(client.Core);
        Assert.NotNull(client.Downloads);
        Assert.NotNull(client.Forums);
    }

    [Fact]
    public void ServersApiClient_ResolvesAndExposesAllSubApisUsedByPortalWeb()
    {
        // Servers client 4.1.14 ships NO cache defaults — .WithCaching(UseLibraryDefaults())
        // would be a no-op, and PortalWebApplication deliberately does not opt in.
        // We still smoke-test composition to catch any regression in the shared
        // cache-scoping wiring that fires across sub-APIs at boot.
        var services = BuildBaseServices();
        services.AddServersApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://servers.test.invalid")
            .WithEntraIdAuthentication("api://servers-test"));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IServersApiClient>();
        Assert.NotNull(client);
        Assert.NotNull(client.Query.V1);
        Assert.NotNull(client.CoD4xRcon.V1);
        Assert.NotNull(client.Cod2Rcon.V1);
        Assert.NotNull(client.Cod4Rcon.V1);
        Assert.NotNull(client.Cod5Rcon.V1);
        Assert.NotNull(client.InsurgencyRcon.V1);
        Assert.NotNull(client.RustRcon.V1);
        Assert.NotNull(client.L4d2Rcon.V1);
        Assert.NotNull(client.Maps.V1);
        Assert.NotNull(client.ApiHealth.V1);
        Assert.NotNull(client.ApiInfo.V1);
        Assert.NotNull(client.Config.V1);
        Assert.NotNull(client.FileBrowse.V1);
        Assert.NotNull(client.Files.V1);
    }

    [Fact]
    public void AllClientsRegisteredTogether_ComposeWithoutThrowing()
    {
        // Exercises the exact registration order PortalWebApplication.cs uses so any
        // interaction between the Invision, Repository, Servers and GeoLocation clients'
        // shared-cache scoping only surfaces here and not at production boot.
        var services = BuildBaseServices();

        services.AddInvisionApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://forums.test.invalid")
            .WithApiKeyAuthentication("test-key", "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter)
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        services.AddRepositoryApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://repository.test.invalid")
            .WithEntraIdAuthentication("api://repository-test"));

        services.AddServersApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://servers.test.invalid")
            .WithEntraIdAuthentication("api://servers-test"));

        services.AddGeoLocationApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://geolocation.test.invalid")
            .WithApiKeyAuthentication("test-key")
            .WithEntraIdAuthentication("api://geolocation-test")
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInvisionApiClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRepositoryApiClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IServersApiClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGeoLocationApiClient>());
    }

    [Fact]
    public void GeoLocationApiClient_WhenLibraryDefaultsEnabled_ResolvesCachingProxyForCachedInterfaces()
    {
        // Proves the caching layer is actually engaged for interfaces that participate
        // in library cache defaults. With caching enabled MX.Api.Client wraps the
        // concrete implementation in a DispatchProxy-generated CachedApiClientProxy;
        // without caching, the raw concrete implementation is resolved directly. If
        // this test starts failing we've silently lost the caching layer.
        var services = BuildBaseServices();
        services.AddGeoLocationApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://geolocation.test.invalid")
            .WithApiKeyAuthentication("test-key")
            .WithEntraIdAuthentication("api://geolocation-test")
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var v1 = scope.ServiceProvider.GetRequiredService<MX.GeoLocation.Abstractions.Interfaces.V1.IGeoLookupApi>();
        var v11 = scope.ServiceProvider.GetRequiredService<MX.GeoLocation.Abstractions.Interfaces.V1_1.IGeoLookupApi>();

        Assert.True(IsCachingProxy(v1), $"Expected caching proxy for V1 IGeoLookupApi, got {v1.GetType().FullName}");
        Assert.True(IsCachingProxy(v11), $"Expected caching proxy for V1.1 IGeoLookupApi, got {v11.GetType().FullName}");
    }

    [Fact]
    public void InvisionApiClient_WhenLibraryDefaultsEnabled_ResolvesCachingProxyForCachedInterfaces()
    {
        var services = BuildBaseServices();
        services.AddInvisionApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://forums.test.invalid")
            .WithApiKeyAuthentication("test-key", "key", MX.Api.Client.Configuration.ApiKeyLocation.QueryParameter)
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var core = scope.ServiceProvider.GetRequiredService<MX.InvisionCommunity.Api.Abstractions.Interfaces.ICoreApi>();
        var downloads = scope.ServiceProvider.GetRequiredService<MX.InvisionCommunity.Api.Abstractions.Interfaces.IDownloadsApi>();
        var forums = scope.ServiceProvider.GetRequiredService<MX.InvisionCommunity.Api.Abstractions.Interfaces.IForumsApi>();

        // ICoreApi (GetCoreHello / GetMember) and IDownloadsApi (GetDownloadFile)
        // participate in library defaults; IForumsApi does not, but MX.Api.Client
        // still wraps every typed sub-API in a caching proxy once caching is enabled
        // at the builder level — that wrapping is what regressed in the original
        // production crash, so we assert all three are proxied.
        Assert.True(IsCachingProxy(core), $"Expected caching proxy for ICoreApi, got {core.GetType().FullName}");
        Assert.True(IsCachingProxy(downloads), $"Expected caching proxy for IDownloadsApi, got {downloads.GetType().FullName}");
        Assert.True(IsCachingProxy(forums), $"Expected caching proxy for IForumsApi, got {forums.GetType().FullName}");
    }

    [Fact]
    public void GeoLocationApiClient_WhenLibraryDefaultsEnabled_ExposesCuratedReadOnlyCachePolicies()
    {
        // Prove the library-shipped read-only cache defaults are actually plumbed
        // through DI when we opt in with .WithCaching(c => c.UseLibraryDefaults()).
        // This is the wiring behind the "cached on second call within TTL" behavior
        // and gives us confidence that TTLs and cached methods match the contract
        // documented in PortalWebApplication.cs.
        var services = BuildBaseServices();
        services.AddGeoLocationApiClient(clientOptions => clientOptions
            .WithBaseUrl("https://geolocation.test.invalid")
            .WithApiKeyAuthentication("test-key")
            .WithEntraIdAuthentication("api://geolocation-test")
            .WithCachePartition(PortalWebApplication.ClientCachePartition)
            .WithCaching(cache => cache.UseLibraryDefaults()));

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var v1Defaults = provider
            .GetRequiredService<DefaultCachePolicies<MX.GeoLocation.Abstractions.Interfaces.V1.IGeoLookupApi>>();
        var v11Defaults = provider
            .GetRequiredService<DefaultCachePolicies<MX.GeoLocation.Abstractions.Interfaces.V1_1.IGeoLookupApi>>();

        // V1 caches the single-hostname GeoLookup.GetGeoLocation lookup only.
        Assert.Contains(v1Defaults.Policies, kvp => kvp.Key.Name == "GetGeoLocation");
        // Batch POST (GetGeoLocations) and DeleteMetadata are intentionally NOT cached.
        Assert.DoesNotContain(v1Defaults.Policies, kvp => kvp.Key.Name == "GetGeoLocations");
        Assert.DoesNotContain(v1Defaults.Policies, kvp => kvp.Key.Name == "DeleteMetadata");

        // V1.1 caches the four single-hostname intelligence lookups portal-web consumes.
        var v11Methods = v11Defaults.Policies.Keys.Select(m => m.Name).ToHashSet();
        Assert.Contains("GetCityGeoLocation", v11Methods);
        Assert.Contains("GetInsightsGeoLocation", v11Methods);
        Assert.Contains("GetProxyCheck", v11Methods);
        Assert.Contains("GetIpIntelligence", v11Methods);
        // Batch GetIpIntelligences POST stays uncached.
        Assert.DoesNotContain("GetIpIntelligences", v11Methods);
    }

    private static ServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>
    /// MX.Api.Client wraps a typed sub-API in a <see cref="DispatchProxy"/>-generated
    /// caching proxy (CachedApiClientProxy&lt;TClient&gt;) whenever any caching is
    /// registered for the parent client. That runtime-generated type is not directly
    /// referenceable, so we detect it structurally by walking the base-type chain
    /// looking for DispatchProxy.
    /// </summary>
    private static bool IsCachingProxy(object instance)
    {
        var type = instance.GetType();
        while (type is not null)
        {
            if (type == typeof(DispatchProxy))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }
}

