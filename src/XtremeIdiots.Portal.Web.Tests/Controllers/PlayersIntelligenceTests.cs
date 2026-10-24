using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Services;
using PlayersApiController = XtremeIdiots.Portal.Web.ApiControllers.PlayersController;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class PlayersIntelligenceTests
{
    private readonly Mock<IAuthorizationService> authorization = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IIpIntelligenceService> intelligence = new();
    private readonly EphemeralDataProtectionProvider protection = new();
    private readonly Guid playerId = Guid.NewGuid();

    public PlayersIntelligenceTests()
    {
        authorization.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.Players_Read))
            .ReturnsAsync(AuthorizationResult.Success());
    }

    private PlayersApiController CreateController()
    {
        var controller = new PlayersApiController(authorization.Object, repository.Object, intelligence.Object, protection,
            new TelemetryClient(new TelemetryConfiguration()), NullLogger<PlayersApiController>.Instance,
            new ConfigurationBuilder().Build(), Mock.Of<IAuditLogger>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Test")) }
            }
        };
        return controller;
    }

    private string Token(DateTime? expires = null)
    {
        return protection.CreateProtector("Players.VisibleIntelligence.v1").Protect(JsonConvert.SerializeObject(
            new PlayersApiController.VisiblePlayersSnapshot(null, expires ?? DateTime.UtcNow.AddMinutes(1),
                [new(playerId, GameType.CallOfDuty4, "203.0.113.10")])));
    }

    [Fact]
    public async Task GridReturnsCoreRowsWithoutWaitingForIntelligence()
    {
        var player = JsonConvert.DeserializeObject<PlayerDto>(
            $$"""{"playerId":"{{playerId}}","gameType":"CallOfDuty4","username":"Core player","guid":"guid","ipAddress":"203.0.113.10"}""")!;
        repository.Setup(x => x.Players.V1.GetPlayers(
            GameType.CallOfDuty4, PlayersFilter.UsernameAndGuid, It.IsAny<string>(), 0, 10,
            It.IsAny<PlayersOrder?>(), PlayerEntityOptions.Tags))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([player]))));
        var controller = CreateController();
        controller.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"draw":1,"start":0,"length":10}"""));

        var result = Assert.IsType<OkObjectResult>(await controller.GetPlayersAjax(GameType.CallOfDuty4, null, null));
        var json = JObject.FromObject(result.Value!);

        Assert.Equal("Core player", json["data"]![0]!["Username"]!.Value<string>());
        Assert.NotEmpty(json["enrichmentToken"]!.Value<string>()!);
        Assert.Equal(JTokenType.Null, json["data"]![0]!["proxyCheckRiskScore"]!.Type);
        intelligence.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VisibleBatchUsesOnlySignedAssociationsWithoutProfileScans()
    {
        intelligence.Setup(x => x.GetAsync("203.0.113.10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult(null, "unavailable", DateTime.UtcNow.AddSeconds(30)));
        var result = Assert.IsType<OkObjectResult>(await CreateController().GetVisibleIntelligence(new(Token(), [playerId])));

        var row = Assert.Single(JObject.FromObject(result.Value!)["data"]!);
        Assert.Equal(playerId, row["PlayerId"]!.Value<Guid>());
        Assert.Equal(JTokenType.Null, row["proxyCheckRiskScore"]!.Type);
        repository.VerifyNoOtherCalls();
        intelligence.Verify(x => x.GetAsync("203.0.113.10", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VisibleBatchRejectsForgedExpiredOversizedAndUnassociatedRequests()
    {
        var controller = CreateController();
        Assert.IsType<BadRequestResult>(await controller.GetVisibleIntelligence(new("forged", [playerId])));
        Assert.IsType<BadRequestResult>(await controller.GetVisibleIntelligence(new(Token(DateTime.UtcNow.AddMinutes(-1)), [playerId])));
        Assert.IsType<BadRequestResult>(await controller.GetVisibleIntelligence(new(Token(), [Guid.NewGuid()])));
        Assert.IsType<BadRequestResult>(await controller.GetVisibleIntelligence(new(Token(), [.. Enumerable.Repeat(playerId, 101)])));
        intelligence.VerifyNoOtherCalls();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VisibleBatchRechecksCurrentGameAuthorization()
    {
        authorization.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4, AuthPolicies.Players_Read))
            .ReturnsAsync(AuthorizationResult.Failed());

        Assert.IsType<ForbidResult>(await CreateController().GetVisibleIntelligence(new(Token(), [playerId])));
        intelligence.VerifyNoOtherCalls();
    }
}
