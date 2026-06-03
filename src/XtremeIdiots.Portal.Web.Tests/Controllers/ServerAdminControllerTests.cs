using System.Net;
using System.Security.Claims;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Screenshots;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class ServerAdminControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IServersApiClient> mockServersApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IGeoLocationApiClient> mockGeoLocationClient = new();
    private readonly Mock<IAdminActionTopics> mockAdminActionTopics = new();
    private readonly Mock<IAgentTelemetryService> mockAgentTelemetryService = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<ServerAdminController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private ServerAdminController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new ServerAdminController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            mockServersApiClient.Object,
            mockGeoLocationClient.Object,
            mockAdminActionTopics.Object,
            mockAgentTelemetryService.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task GetScreenshots_ForwardsFiltersToRepositoryClient()
    {
        var serverId = Guid.NewGuid();
        var capturedFromUtc = DateTime.UtcNow.AddHours(-2);
        var capturedToUtc = DateTime.UtcNow.AddHours(-1);
        GetScreenshotsQuery? capturedQuery = null;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.Screenshots.V1.GetScreenshots(
                serverId,
                It.IsAny<int>(),
                It.IsAny<int>(),
                ScreenshotOrder.CapturedUtcDesc,
                It.IsAny<CancellationToken>(),
                It.IsAny<GetScreenshotsQuery>()))
            .Callback<Guid, int, int, ScreenshotOrder?, CancellationToken, GetScreenshotsQuery?>((_, _, _, _, _, query) => capturedQuery = query)
            .ReturnsAsync(new ApiResult<CollectionModel<ScreenshotDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<ScreenshotDto>>(new CollectionModel<ScreenshotDto>([]))));

        var sut = CreateSut();

        var result = await sut.GetScreenshots(
            serverId,
            skipEntries: 10,
            takeEntries: 25,
            playerIdentifier: "123",
            playerName: "alpha",
            capturedFromUtc: capturedFromUtc,
            capturedToUtc: capturedToUtc,
            source: "agent-monitor",
            includeDeleted: false,
            cancellationToken: CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        Assert.NotNull(capturedQuery);
        Assert.Equal("123", capturedQuery.PlayerIdentifier);
        Assert.Equal("alpha", capturedQuery.PlayerName);
        Assert.Equal(capturedFromUtc, capturedQuery.CapturedFromUtc);
        Assert.Equal(capturedToUtc, capturedQuery.CapturedToUtc);
        Assert.Equal("agent-monitor", capturedQuery.Source);
        Assert.False(capturedQuery.IncludeDeleted);
    }

    [Fact]
    public async Task GetScreenshots_IncludeDeletedWithoutDeletePermission_ReturnsUnauthorized()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Delete))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, includeDeleted: true, cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    private static GameServerDto CreateGameServerDto(Guid gameServerId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "Test Server",
            GameType = GameType.CallOfDuty4,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = true,
            FileTransportEnabled = true,
            FileTransportType = "Ftp",
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }
}
