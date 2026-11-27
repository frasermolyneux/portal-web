using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class PlayerActivityTests
{
    private const GameType PlayerGame = GameType.CallOfDuty4;
    private readonly static DateTime ToUtc = new(2026, 11, 26, 22, 0, 0, DateTimeKind.Utc);
    private readonly static DateTime FromUtc = ToUtc.AddDays(-90);
    private readonly Guid playerId = Guid.NewGuid();
    private readonly Guid allowedServerId = Guid.NewGuid();
    private readonly Guid deniedServerId = Guid.NewGuid();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> authorization = new();

    public PlayerActivityTests()
    {
        var player = JsonConvert.DeserializeObject<PlayerDto>(
            JsonConvert.SerializeObject(new { PlayerId = playerId, GameType = PlayerGame }))!;
        repository.Setup(api => api.Players.V1.GetPlayer(playerId, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.OK, new ApiResponse<PlayerDto>(player)));

        var servers = new CollectionModel<GameServerDto>([Server(allowedServerId), Server(deniedServerId)]);
        repository.Setup(api => api.GameServers.V1.GetGameServers(It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), null,
                0, It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<GameServerDto>>(servers)));

        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), PlayerGame, AuthPolicies.Players_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), ValueTuple.Create(PlayerGame, allowedServerId), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), ValueTuple.Create(PlayerGame, deniedServerId), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Failed());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllServers_OnlyRequestsAuthorizedServerAndChatAccess(bool chatAllowed)
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), PlayerGame, AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(chatAllowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        var activity = JsonConvert.DeserializeObject<PlayerActivityDto>("{}")!;
        repository.Setup(api => api.PlayerAnalyticsV2.V1.GetPlayerActivity(playerId, FromUtc, ToUtc,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(allowedServerId)), chatAllowed, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PlayerActivityDto>(HttpStatusCode.OK, new ApiResponse<PlayerActivityDto>(activity)));

        var result = Assert.IsType<OkObjectResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, null));
        var resultJson = System.Text.Json.JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var availableServers = resultJson.GetProperty("availableServers").EnumerateArray().ToArray();
        Assert.Single(availableServers);
        Assert.Equal(allowedServerId, availableServers[0].GetProperty("serverId").GetGuid());
    }

    [Fact]
    public async Task ServerScope_UsesAllPagesWhenTheApiReturnsAShortPage()
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), ValueTuple.Create(PlayerGame, deniedServerId), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        repository.Setup(api => api.GameServers.V1.GetGameServers(It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), null,
                0, It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServerPage([Server(allowedServerId)], 0));
        repository.Setup(api => api.GameServers.V1.GetGameServers(It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), null,
                1, It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServerPage([Server(deniedServerId)], 1));
        repository.Setup(api => api.PlayerAnalyticsV2.V1.GetPlayerActivity(playerId, FromUtc, ToUtc,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(allowedServerId) && ids.Contains(deniedServerId)),
                false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PlayerActivityDto>(HttpStatusCode.OK,
                new ApiResponse<PlayerActivityDto>(JsonConvert.DeserializeObject<PlayerActivityDto>("{}")!)));
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), PlayerGame, AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Failed());

        var result = Assert.IsType<OkObjectResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, null));
        var resultJson = System.Text.Json.JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(2, resultJson.GetProperty("availableServers").GetArrayLength());
    }

    [Fact]
    public async Task ForbiddenSelection_DoesNotFallBackToAllServers()
    {
        Assert.IsType<ForbidResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, deniedServerId));
        VerifyNoActivityRequest();
    }

    [Fact]
    public async Task SelectedServer_DoesNotIncludeOtherAuthorizedServers()
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), ValueTuple.Create(PlayerGame, deniedServerId), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), PlayerGame, AuthPolicies.ChatLog_ReadServer))
            .ReturnsAsync(AuthorizationResult.Failed());
        repository.Setup(api => api.PlayerAnalyticsV2.V1.GetPlayerActivity(playerId, FromUtc, ToUtc,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(allowedServerId)),
                false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PlayerActivityDto>(HttpStatusCode.OK,
                new ApiResponse<PlayerActivityDto>(JsonConvert.DeserializeObject<PlayerActivityDto>("{}")!)));

        var result = Assert.IsType<OkObjectResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, allowedServerId));
        var resultJson = System.Text.Json.JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var availableServers = resultJson.GetProperty("availableServers").EnumerateArray().ToArray();
        Assert.Single(availableServers);
        Assert.Equal(allowedServerId, availableServers[0].GetProperty("serverId").GetGuid());
    }

    [Fact]
    public async Task NoAuthorizedServers_DoesNotRequestAnUnscopedActivity()
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), ValueTuple.Create(PlayerGame, allowedServerId), AuthPolicies.GameServers_Admin_Read))
            .ReturnsAsync(AuthorizationResult.Failed());

        Assert.IsType<ForbidResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, null));
        VerifyNoActivityRequest();
    }

    [Fact]
    public async Task NoServersForPlayerGame_IsUnavailableWithoutCallingActivity()
    {
        repository.Setup(api => api.GameServers.V1.GetGameServers(It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), null,
                0, It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

        var result = Assert.IsType<ObjectResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, null));
        Assert.Equal(503, result.StatusCode);
        VerifyNoActivityRequest();
    }

    [Fact]
    public async Task DeniedPlayer_DoesNotRequestServerScope()
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), PlayerGame, AuthPolicies.Players_Read))
            .ReturnsAsync(AuthorizationResult.Failed());

        Assert.IsType<ForbidResult>(await Controller().PlayerActivity(playerId, FromUtc, ToUtc, null));
        repository.Verify(api => api.GameServers.V1.GetGameServers(
            It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), It.IsAny<GameServerFilter?>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<GameServerOrder?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoActivityRequest();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task InvalidWindow_IsRejectedBeforeReadingPlayer(int days)
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().PlayerActivity(playerId, ToUtc.AddDays(-days), ToUtc, null));
        repository.Verify(api => api.Players.V1.GetPlayer(It.IsAny<Guid>(), It.IsAny<PlayerEntityOptions>()), Times.Never);
    }

    private void VerifyNoActivityRequest()
        => repository.Verify(api => api.PlayerAnalyticsV2.V1.GetPlayerActivity(
            It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

    private AnalyticsController Controller()
        => new(repository.Object, authorization.Object, new TelemetryClient(new TelemetryConfiguration()),
            NullLogger<AnalyticsController>.Instance, Mock.Of<IConfiguration>(), Mock.Of<IAuditLogger>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Test")) }
            }
        };

    private static GameServerDto Server(Guid id)
        => JsonConvert.DeserializeObject<GameServerDto>(
            JsonConvert.SerializeObject(new { GameServerId = id, GameType = PlayerGame }))!;

    private static ApiResult<CollectionModel<GameServerDto>> ServerPage(GameServerDto[] servers, int skip)
        => new(HttpStatusCode.OK, new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers))
        {
            Pagination = new ApiPagination(2, 2, skip, 100)
        });
}
