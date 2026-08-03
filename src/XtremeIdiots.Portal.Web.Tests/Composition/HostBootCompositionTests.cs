using Microsoft.Extensions.DependencyInjection;

using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Tests.Composition;

/// <summary>
/// Host-boot smoke test that mirrors PortalWebApplication.cs's repository client
/// registration (see <see cref="PortalWebApplication"/>, the <c>AddRepositoryApiClient</c>
/// call) and asserts DI composition succeeds.
///
/// This is a "never again" regression guard: the original portal-web production
/// crash was a cross-sub-API cache-scoping failure inside the client library that
/// manifested at DI-composition time. This test builds the same registration
/// graph, calls <see cref="ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(IServiceCollection)"/>,
/// and resolves <see cref="IRepositoryApiClient"/> plus every typed sub-API surface
/// portal-web actually uses. If any future client / MX.Api.Client release
/// reintroduces a composition-time crash, this test fails before the app boots.
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
}
