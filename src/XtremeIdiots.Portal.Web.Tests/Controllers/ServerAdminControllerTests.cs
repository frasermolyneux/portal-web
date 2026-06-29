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
            .Setup(x => x.CoD4xRcon.V1.ConSay(
                serverId,
                It.Is<CoD4xMessageRequestDto>(r => r.Message == longMessage),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.OK, new ApiResponse<string>("ok")));

        var sut = CreateSut();

        var result = await sut.SendSayCommand(serverId, inputMessage, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.ConSay(
            serverId,
            It.Is<CoD4xMessageRequestDto>(r => r.Message == longMessage),
            It.IsAny<CancellationToken>()), Times.Once);
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

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.ConSay(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xMessageRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ServerDetail_WhenAuthorized_ShowsFeedEventsFlag()
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

    [Fact]
    public async Task TempBanRconPlayer_CoD4xWithGuid_UsesCoD4xIdentifierEndpoint()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
                serverId,
                It.IsAny<CoD4xTempBanRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CoD4xBanCommandResponseDto>(
                HttpStatusCode.OK,
                new ApiResponse<CoD4xBanCommandResponseDto>(new CoD4xBanCommandResponseDto { IsSuccess = true })));

        var sut = CreateSut();

        var result = await sut.TempBanRconPlayer(serverId, 4, "guid-abc", "PlayerOne", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
            serverId,
            It.Is<CoD4xTempBanRequestDto>(r =>
                r.PlayerIdentifier == "guid-abc" &&
                r.DurationMinutes > 0),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.Rcon.V1.TempBanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        mockServersApiClient.Verify(x => x.Rcon.V1.BanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BanRconPlayer_CoD4xWithGuid_UsesCoD4xIdentifierEndpoint()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
                serverId,
                It.IsAny<CoD4xPermBanRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CoD4xBanCommandResponseDto>(
                HttpStatusCode.OK,
                new ApiResponse<CoD4xBanCommandResponseDto>(new CoD4xBanCommandResponseDto { IsSuccess = true })));

        var sut = CreateSut();

        var result = await sut.BanRconPlayer(serverId, 9, "guid-def", "PlayerTwo", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            serverId,
            It.Is<CoD4xPermBanRequestDto>(r => r.PlayerIdentifier == "guid-def"),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.Rcon.V1.BanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TempBanRconPlayer_CoD4xIdentifierFailure_ReturnsFailureWithoutGenericFallback()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
                serverId,
                It.IsAny<CoD4xTempBanRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CoD4xBanCommandResponseDto>(
                HttpStatusCode.OK,
                new ApiResponse<CoD4xBanCommandResponseDto>(new CoD4xBanCommandResponseDto { IsSuccess = false })));

        var sut = CreateSut();

        var result = await sut.TempBanRconPlayer(serverId, 4, "guid-abc", "PlayerOne", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
            serverId,
            It.IsAny<CoD4xTempBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.Rcon.V1.TempBanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        mockServersApiClient.Verify(x => x.Rcon.V1.BanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BanRconPlayer_CoD4xIdentifierFailure_ReturnsFailureWithoutGenericFallback()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
                serverId,
                It.IsAny<CoD4xPermBanRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CoD4xBanCommandResponseDto>(
                HttpStatusCode.OK,
                new ApiResponse<CoD4xBanCommandResponseDto>(new CoD4xBanCommandResponseDto { IsSuccess = false })));

        var sut = CreateSut();

        var result = await sut.BanRconPlayer(serverId, 9, "guid-def", "PlayerTwo", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            serverId,
            It.IsAny<CoD4xPermBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockServersApiClient.Verify(x => x.Rcon.V1.BanPlayer(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
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
