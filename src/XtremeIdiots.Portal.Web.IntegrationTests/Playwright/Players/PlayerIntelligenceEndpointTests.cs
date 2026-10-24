using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Abstractions.Models.V1_1;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

[Trait("Category", "HttpIntegration")]
public sealed class PlayerIntelligenceEndpointTests
{
    [Fact]
    public async Task OptionalDependencyDoesNotBlockPlayerShellAcrossRequests()
    {
        var player = new PlayerDtoBuilder().Build();
        var scenario = new PlayersDetailsScenario(player);
        var gate = new RequestGate(hold: true);
        scenario.GeoLocationClient.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence(player.IpAddress, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                await gate.WaitAsync(token);
                return new ApiResult<IpIntelligenceDto>(HttpStatusCode.NotFound);
            });
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var shell = await host.Client.GetAsync($"/Players/Details/{player.PlayerId}").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Contains("Loading IP intelligence", await shell.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        scenario.GeoLocationClient.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var optional = host.Client.GetAsync($"/Players/GetIntelligence/{player.PlayerId}?ipAddress={Uri.EscapeDataString(player.IpAddress)}");
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            using var secondShell = await host.Client.GetAsync($"/Players/Details/{player.PlayerId}").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, secondShell.StatusCode);
        }
        finally
        {
            gate.Release();
        }

        using var result = await optional;
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("IP intelligence is unavailable", await result.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlayerIntelligenceRejectsUnassociatedIpBeforeCallingGeoDependency()
    {
        var player = new PlayerDtoBuilder().Build();
        var scenario = new PlayersDetailsScenario(player);
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync($"/Players/GetIntelligence/{player.PlayerId}?ipAddress=198.51.100.9");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Errors/Display/404", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        scenario.GeoLocationClient.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IpDetailShellIsIndependentAndOptionalFragmentPreservesRichIntelligence()
    {
        var player = new PlayerDtoBuilder().Build();
        var scenario = new PlayersDetailsScenario(player);
        scenario.RepositoryClient.Setup(x => x.Players.V1.GetPlayersWithIpAddress(
                player.IpAddress, 0, 100, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([player]))));
        var data = JsonConvert.DeserializeObject<IpIntelligenceDto>(
                                 /*lang=json,strict*/
                                 """{"countryCode":"GB","countryName":"United Kingdom","cityName":"London","timezone":"Europe/London","latitude":51.5,"longitude":-0.1,"maxMindStatus":"Success","proxyCheckStatus":"Success","proxyCheck":{"riskScore":80,"isProxy":true,"proxyType":"Hosting","country":"United Kingdom","region":"London","asNumber":"AS64500","asOrganization":"Example Network"}}""");
        scenario.GeoLocationClient.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence(player.IpAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<IpIntelligenceDto>(HttpStatusCode.OK, new ApiResponse<IpIntelligenceDto>(data)));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var shell = await host.Client.GetAsync($"/IPAddresses/Details?ipAddress={player.IpAddress}");
        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Contains(player.Username, await shell.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        scenario.GeoLocationClient.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        using var optional = await host.Client.GetAsync($"/IPAddresses/GetIntelligence?ipAddress={player.IpAddress}");
        Assert.Equal(HttpStatusCode.OK, optional.StatusCode);
        var html = await optional.Content.ReadAsStringAsync();
        foreach (var field in new[] { "United Kingdom", "Europe/London", "London", "AS64500", "Example Network", "Hosting", "Risk Score: 80" })
            Assert.Contains(field, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IpIntelligenceCannotBeUsedAsAnArbitraryIpProxy()
    {
        var scenario = new PlayersDetailsScenario(new PlayerDtoBuilder().Build());
        scenario.RepositoryClient.Setup(x => x.Players.V1.GetPlayersWithIpAddress(
                "198.51.100.9", 0, 100, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([]))));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync("/IPAddresses/GetIntelligence?ipAddress=198.51.100.9");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Errors/Display/404", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        scenario.GeoLocationClient.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
