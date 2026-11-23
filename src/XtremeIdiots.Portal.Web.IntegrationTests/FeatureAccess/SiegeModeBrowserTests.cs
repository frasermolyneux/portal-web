using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "Browser")]
public sealed class SiegeModeBrowserTests
{
    [Fact]
    public async Task Activation_RequiresExplicitModeAndUsesDisplayedDuration()
    {
        var id = Guid.NewGuid();
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        var server = JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            GameType = GameType.CallOfDuty4,
            Title = "Production test server"
        }))!;
        repository.Setup(r => r.GameServers.V1.GetGameServer(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, null, 0, 100,
                GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration("siegeMode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.NotFound));
        repository.Setup(r => r.SiegeMode.V1.StartSession(id, It.IsAny<StartSiegeSessionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.Created,
                new ApiResponse<SiegeSessionDto>(new SiegeSessionDto { GameServerId = id, Mode = SiegeSessionMode.DryRun })));
        var sessionId = Guid.NewGuid();
        repository.Setup(r => r.SiegeMode.V1.GetCurrentSession(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.OK, new ApiResponse<SiegeSessionDto>(
                new SiegeSessionDto { GameServerId = id, SessionId = sessionId, Mode = SiegeSessionMode.DryRun, ExpiresAtUtc = DateTime.UtcNow.AddHours(1) })));
        repository.Setup(r => r.SiegeMode.V1.GetActions(id, sessionId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<SiegeActionDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<SiegeActionDto>>(new CollectionModel<SiegeActionDto>([]))));
        repository.Setup(r => r.SiegeMode.V1.GetChecks(id, sessionId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<SiegeCheckDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<SiegeCheckDto>>(new CollectionModel<SiegeCheckDto>([]))));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
        });
        await browser.Page.RouteAsync("**/ServerAdmin/ServerDetail/**", route => route.FulfillAsync(new RouteFulfillOptions { Body = "Server detail" }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/SiegeMode/Activate/{id}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByText("Production test server")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByText("240 minutes")).ToBeVisibleAsync();
        Assert.False(await browser.Page.Locator("form[action*='/SiegeMode/Activate']").EvaluateAsync<bool>("form => form.checkValidity()"));

        await browser.Page.EvaluateAsync("""
            id => {
                const status = document.createElement('div');
                status.id = 'siege-status';
                status.dataset.url = '/SiegeMode/Status/' + id;
                document.body.append(status);
            }
            """, id.ToString());
        await browser.Page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = new Uri(browser.Host.BaseAddress, "/js/siege-mode.js").AbsoluteUri });
        await Assertions.Expect(browser.Page.Locator("#siege-status")).ToContainTextAsync("Siege Mode active: Dry Run");
        await Assertions.Expect(browser.Page.Locator("#siege-status form input[name='__RequestVerificationToken']")).ToHaveCountAsync(2);
        await Assertions.Expect(browser.Page.Locator("#siege-extension-minutes")).ToBeVisibleAsync();

        await browser.Page.GetByLabel("Dry Run", new() { Exact = false }).CheckAsync();
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Activate selected mode" }).ClickAsync(),
            r => r.Request.Method == "POST" && r.Url.Contains("/SiegeMode/Activate/", StringComparison.Ordinal));
        Assert.Equal(302, response.Status);
        repository.Verify(r => r.SiegeMode.V1.StartSession(id,
            It.Is<StartSiegeSessionDto>(s => s.Mode == SiegeSessionMode.DryRun &&
                s.ExpectedDurationMinutes == 240 && s.Actor == "12345" && s.OperationId != Guid.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
