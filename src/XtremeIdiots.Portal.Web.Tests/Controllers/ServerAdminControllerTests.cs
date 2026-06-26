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
using Newtonsoft.Json.Linq;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ChatMessages;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Screenshots;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

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
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

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
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

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

    [Fact]
    public async Task GetScreenshots_IncludeDeletedScopedAuthorizationFails_ReturnsUnauthorized()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object>(),
                AuthPolicies.GameServers_Admin_Screenshots_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object>(resource => resource is Web.Auth.PotentialAccessProbe),
                AuthPolicies.GameServers_Admin_Screenshots_Delete))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object>(resource => resource is GameType),
                AuthPolicies.GameServers_Admin_Screenshots_Delete))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, includeDeleted: true, cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetScreenshots_WhenGetGameServerTimesOut_ReturnsEmptyData()
    {
        var serverId = Guid.NewGuid();

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Simulated timeout"));

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, cancellationToken: CancellationToken.None);

        AssertJsonDataIsEmpty(result);
    }

    [Fact]
    public async Task GetScreenshots_WhenGetScreenshotsTimesOut_ReturnsEmptyData()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

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
            .ThrowsAsync(new TaskCanceledException("Simulated timeout"));

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, cancellationToken: CancellationToken.None);

        AssertJsonDataIsEmpty(result);
    }

    [Fact]
    public async Task GetScreenshots_WhenRequestIsCanceled_ReturnsEmptyData()
    {
        var serverId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("User canceled request"));

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, cancellationToken: cancellationSource.Token);

        AssertJsonDataIsEmpty(result);
    }

    [Fact]
    public async Task GetScreenshots_WhenScreenshotsRequestIsCanceled_ReturnsEmptyData()
    {
        var serverId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

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
            .ThrowsAsync(new TaskCanceledException("User canceled request"));

        var sut = CreateSut();

        var result = await sut.GetScreenshots(serverId, cancellationToken: cancellationSource.Token);

        AssertJsonDataIsEmpty(result);
    }

    [Fact]
    public async Task TakeRconScreenshot_WhenPendingRequestFails_ReturnsFailureAndDoesNotInvokeRcon()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon_Screenshot))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.Screenshots.V1.CreatePendingScreenshotRequest(It.IsAny<CreatePendingScreenshotRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PendingScreenshotRequestDto>(HttpStatusCode.BadRequest));

        var sut = CreateSut();

        var result = await sut.TakeRconScreenshot(serverId, "player-guid-001", "Alpha", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));

        mockServersApiClient.Verify(
            x => x.Rcon.V1.TakeScreenshot(It.IsAny<Guid>(), It.IsAny<TakeScreenshotRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TakeRconScreenshot_WhenPendingRequestSucceeds_CreatesPendingAndInvokesRconScreenshot()
    {
        var serverId = Guid.NewGuid();
        var pendingRequests = new List<CreatePendingScreenshotRequestDto>();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon_Screenshot))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.Screenshots.V1.CreatePendingScreenshotRequest(It.IsAny<CreatePendingScreenshotRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreatePendingScreenshotRequestDto, CancellationToken>((request, _) => pendingRequests.Add(request))
            .ReturnsAsync(new ApiResult<PendingScreenshotRequestDto>(HttpStatusCode.OK, new ApiResponse<PendingScreenshotRequestDto>(new PendingScreenshotRequestDto())));

        mockServersApiClient
            .Setup(x => x.Rcon.V1.TakeScreenshot(serverId, It.IsAny<TakeScreenshotRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        var sut = CreateSut();

        var result = await sut.TakeRconScreenshot(serverId, "  player-guid-002  ", "  Bravo  ", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        Assert.Equal(2, pendingRequests.Count);

        var initialPendingRequest = pendingRequests[0];
        Assert.Equal(serverId, initialPendingRequest.GameServerId);
        Assert.Equal("player-guid-002", initialPendingRequest.PlayerIdentifier);
        Assert.Equal("Bravo", initialPendingRequest.PlayerName);
        Assert.NotNull(initialPendingRequest.RequestedAtUtc);
        Assert.NotNull(initialPendingRequest.ExpiresAtUtc);

        var confirmedPendingRequest = pendingRequests[1];
        Assert.Equal(serverId, confirmedPendingRequest.GameServerId);
        Assert.Equal("player-guid-002", confirmedPendingRequest.PlayerIdentifier);
        Assert.Equal("Bravo", confirmedPendingRequest.PlayerName);
        Assert.NotNull(confirmedPendingRequest.RequestedAtUtc);
        Assert.NotNull(confirmedPendingRequest.ExpiresAtUtc);

        var initialLifetime = initialPendingRequest.ExpiresAtUtc.Value - initialPendingRequest.RequestedAtUtc.Value;
        var confirmedLifetime = confirmedPendingRequest.ExpiresAtUtc.Value - confirmedPendingRequest.RequestedAtUtc.Value;
        Assert.True(confirmedLifetime > initialLifetime);

        mockServersApiClient.Verify(
            x => x.Rcon.V1.TakeScreenshot(
                serverId,
                It.Is<TakeScreenshotRequestDto>(request => request.PlayerIdentifier == "player-guid-002"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendSayCommand_WithLongMessage_ForwardsUntruncatedTrimmedMessage()
    {
        var serverId = Guid.NewGuid();
        var longMessage = new string('A', 400);
        var inputMessage = $"  {longMessage}  ";

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon_Say))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Rcon.V1.Say(serverId, longMessage))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        var sut = CreateSut();

        var result = await sut.SendSayCommand(serverId, inputMessage, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.Rcon.V1.Say(serverId, longMessage), Times.Once);
    }

    [Fact]
    public async Task SendSayCommand_WithWhitespaceMessage_ReturnsValidationFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Rcon_Say))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.SendSayCommand(serverId, "   ", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("Message cannot be empty", payload.Value<string>("message"));

        mockServersApiClient.Verify(x => x.Rcon.V1.Say(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ServerDetail_WhenGameTypeIsNotCallOfDuty4x_HidesScreenshotFeatures()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4))));

        mockRepositoryApiClient
            .Setup(x => x.LiveStatus.V1.GetGameServerLiveStatus(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<Repository.Abstractions.Models.V1.LiveStatus.GameServerLiveStatusDto>(HttpStatusCode.OK));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.ServerDetail(serverId, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServerDetailViewModel>(viewResult.Model);

        Assert.False(model.CanViewScreenshots);
        Assert.False(model.CanTakeScreenshot);
    }

    [Fact]
    public async Task ServerDetail_WhenGameTypeIsCallOfDuty4x_ShowsScreenshotFeaturesWithPermission()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockRepositoryApiClient
            .Setup(x => x.LiveStatus.V1.GetGameServerLiveStatus(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<Repository.Abstractions.Models.V1.LiveStatus.GameServerLiveStatusDto>(HttpStatusCode.OK));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.ServerDetail(serverId, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServerDetailViewModel>(viewResult.Model);

        Assert.True(model.CanViewScreenshots);
        Assert.True(model.CanTakeScreenshot);
        Assert.True(model.CanViewFeedEvents);
    }

    [Fact]
    public async Task GetServerFeed_WhenServerDoesNotExist_ReturnsNotFound()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));

        var sut = CreateSut();

        var result = await sut.GetServerFeed(serverId, cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetServerFeed_WhenChatAuthorizationFails_ReturnsUnauthorized()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var result = await sut.GetServerFeed(serverId, cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetServerFeed_WhenEventsAuthorizationFails_ReturnsChatOnlyAndEventsDenied()
    {
        var serverId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockRepositoryApiClient
            .Setup(x => x.ChatMessages.V1.GetChatMessages(
                null,
                serverId,
                null,
                null,
                0,
                200,
                ChatMessageOrder.TimestampDesc,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ChatMessageDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ChatMessageDto>>(new CollectionModel<ChatMessageDto>(
                [
                    CreateChatMessageDto(Guid.NewGuid(), serverId, timestamp)
                ]))));

        var sut = CreateSut();

        var result = await sut.GetServerFeed(serverId, cancellationToken: CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));

        Assert.False(payload["SourceAuthorization"]?["EventsAllowed"]?.Value<bool>());

        var items = payload["Items"] as JArray;
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("chat", items[0]?["SourceType"]?.Value<string>());

        mockRepositoryApiClient.Verify(
            x => x.GameServersEvents.V1.GetGameServerEvents(
                It.IsAny<GameType?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerEventOrder?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetServerFeed_WhenCursorIsProvided_ReturnsOnlyNewerItems()
    {
        var serverId = Guid.NewGuid();
        var olderTimestamp = DateTime.UtcNow.AddMinutes(-2);
        var newerTimestamp = DateTime.UtcNow.AddMinutes(-1);
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.ChatMessages.V1.GetChatMessages(
                null,
                serverId,
                null,
                null,
                0,
                200,
                ChatMessageOrder.TimestampDesc,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ChatMessageDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ChatMessageDto>>(new CollectionModel<ChatMessageDto>(
                [
                    CreateChatMessageDto(newerId, serverId, newerTimestamp),
                    CreateChatMessageDto(olderId, serverId, olderTimestamp)
                ]))));

        var sut = CreateSut();

        var result = await sut.GetServerFeed(
            serverId,
            lastSeenTimestampUtc: olderTimestamp,
            lastSeenSourceType: "chat",
            lastSeenItemId: BuildFeedItemId("chat", olderId),
            includeEvents: false,
            cancellationToken: CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        var items = payload["Items"] as JArray;

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(BuildFeedItemId("chat", newerId), items[0]?["ItemId"]?.Value<string>());
    }

    [Fact]
    public async Task GetServerFeed_WhenEventPayloadIsPlainText_RedactsSensitiveFields()
    {
        var serverId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.GameServersEvents.V1.GetGameServerEvents(
                null,
                serverId,
                null,
                0,
                200,
                GameServerEventOrder.TimestampDesc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerEventDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerEventDto>>(new CollectionModel<GameServerEventDto>(
                [
                    CreateGameServerEventDto(eventId, serverId, timestamp, "token=abc123; message=ok")
                ]))));

        var sut = CreateSut();

        var result = await sut.GetServerFeed(serverId, includeChat: false, includeEvents: true, cancellationToken: CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        var rawEventData = payload["Items"]?[0]?["RawEventData"]?.Value<string>();

        Assert.NotNull(rawEventData);
        Assert.DoesNotContain("abc123", rawEventData, StringComparison.Ordinal);
        Assert.Contains("token=***", rawEventData, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertJsonDataIsEmpty(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(json.Value));
        var dataToken = payload["data"];
        Assert.NotNull(dataToken);
        Assert.Equal(JTokenType.Array, dataToken.Type);
        Assert.False(dataToken.HasValues);
    }

    private static GameServerDto CreateGameServerDto(Guid gameServerId, GameType gameType = GameType.CallOfDuty4)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "Test Server",
            GameType = gameType,
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

    private static ChatMessageDto CreateChatMessageDto(Guid chatMessageId, Guid gameServerId, DateTime timestampUtc)
    {
        var json = JsonConvert.SerializeObject(new
        {
            ChatMessageId = chatMessageId,
            GameServerId = gameServerId,
            PlayerId = Guid.NewGuid(),
            Username = "TestPlayer",
            ChatType = "All",
            Message = "Test message",
            Timestamp = timestampUtc,
            Locked = false,
            Player = new { PlayerId = Guid.NewGuid(), Username = "TestPlayer", GameType = GameType.CallOfDuty4x },
            GameServer = new
            {
                GameServerId = gameServerId,
                Title = "Test Server",
                GameType = GameType.CallOfDuty4x,
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
            }
        });

        return JsonConvert.DeserializeObject<ChatMessageDto>(json)!;
    }

    private static GameServerEventDto CreateGameServerEventDto(Guid eventId, Guid gameServerId, DateTime timestampUtc, string? eventData)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerEventId = eventId,
            GameServerId = gameServerId,
            Timestamp = timestampUtc,
            EventType = "TestEvent",
            EventData = eventData,
            GameServer = new
            {
                GameServerId = gameServerId,
                Title = "Test Server",
                GameType = GameType.CallOfDuty4x,
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
            }
        });

        return JsonConvert.DeserializeObject<GameServerEventDto>(json)!;
    }

    private static string BuildFeedItemId(string sourceType, Guid id)
    {
        return $"{sourceType}:{id:N}";
    }
}
