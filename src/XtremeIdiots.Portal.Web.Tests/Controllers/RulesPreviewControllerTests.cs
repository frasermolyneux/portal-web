using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using NewtonsoftJson = Newtonsoft.Json.JsonConvert;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class RulesPreviewControllerTests
{
    private readonly Mock<IAuthorizationService> authorization = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IServersApiClient> servers = new() { DefaultValue = DefaultValue.Mock };
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    private RulesPreviewController CreateController(bool allowed = true)
    {
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AuthPolicies.GlobalSettings_Admin))
            .ReturnsAsync(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return new RulesPreviewController(repository.Object, authorization.Object, servers.Object, cache, NullLogger<RulesPreviewController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
            }
        };
    }

    private static RulesPreviewRequest Request(string json, params string[] templates) => new()
    {
        FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature,
        ActionTypeId = "message.private",
        ParameterName = "message",
        Mode = "sample",
        Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!,
        TemplateParameters = [.. templates]
    };

    private RulesPreviewRequest ServerRequest(Guid serverId, string message, string actionTypeId = "message.private")
    {
        var server = NewtonsoftJson.DeserializeObject<GameServerDto>(NewtonsoftJson.SerializeObject(new
        {
            GameServerId = serverId,
            GameType = GameType.CallOfDuty4,
            Title = "Test server",
            Hostname = "127.0.0.1",
            QueryPort = 28960
        }))!;
        repository.Setup(client => client.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4, AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());
        return new RulesPreviewRequest
        {
            GameServerId = serverId,
            FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature,
            ActionTypeId = actionTypeId,
            ParameterName = "message",
            Mode = "sample",
            Parameters = new Dictionary<string, JsonElement> { ["message"] = JsonSerializer.SerializeToElement(message) },
            TemplateParameters = []
        };
    }

    [Fact]
    public async Task Preview_RequiresGlobalSettingsPermission()
    {
        var result = await CreateController(false).Preview(Request(/*lang=json,strict*/ """{"message":"hello"}"""), default);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Preview_RequiresWritePermissionForTheRequestedServerGameType()
    {
        var serverId = Guid.NewGuid();
        var server = NewtonsoftJson.DeserializeObject<GameServerDto>(NewtonsoftJson.SerializeObject(new
        {
            GameServerId = serverId,
            GameType = GameType.CallOfDuty4,
            Title = "Test server",
            Hostname = "127.0.0.1",
            QueryPort = 28960
        }))!;
        repository.Setup(client => client.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4, AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Failed());
        var request = new RulesPreviewRequest { GameServerId = serverId };

        Assert.IsType<ForbidResult>(await CreateController().Preview(request, default));
        servers.Verify(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Preview_UsesSharedRendererAndFragmentationForOptedInListOnly()
    {
        var request = Request(/*lang=json,strict*/ """{"message":"literal {context.player.Name}","messages":["hello {context.player.Name}","next"]}""", "messages");
        var result = Assert.IsType<JsonResult>(await CreateController().Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;
        Assert.Empty(root.GetProperty("errors").EnumerateArray());
        var fragments = root.GetProperty("fragments");
        var rendered = RuleTextRenderer.Render("hello {context.player.Name}", RuleTextTokens.CreateSampleValues());
        var expected = MessagePreparation.Prepare("literal {context.player.Name}", [rendered.Text!, "next"],
            new MessageChannelBudget("message.private", 149));
        Assert.Equal(expected.Fragments.Select(fragment => fragment.Text),
            fragments.EnumerateArray().Select(fragment => fragment.GetProperty("text").GetString()));
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Preview_ReportsInvalidTokensWithoutProducingFragments()
    {
        var result = Assert.IsType<JsonResult>(await CreateController().Preview(
            Request(/*lang=json,strict*/ """{"message":"hello {unknown.token}"}""", "message"), default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("fragments").EnumerateArray());
    }

    [Fact]
    public async Task Preview_RejectsTemplateOptInForUnsupportedParameter()
    {
        var result = await CreateController().Preview(Request(/*lang=json,strict*/ """{"message":"hello"}""", "recipientTags"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Preview_UsesServerRconBudgetAndCachesItBriefly()
    {
        var serverId = Guid.NewGuid();
        var request = ServerRequest(serverId, new string('A', 120));
        var budget = new RuleMessageChannelBudgetDto
        {
            Status = RuleMessageBudgetStatus.Available,
            Budget = new MessageChannelBudget(RuleMessageChannels.RconPrivateChat, 149, "rcon", "Test evidence", 32, "CallOfDuty4"),
            Constraints = new RconMessageCharacterConstraintsDto { AsciiOnly = true },
            Detail = "Test evidence"
        };
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                serverId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.OK, new ApiResponse<RuleMessageChannelBudgetDto>(budget)));
        var controller = CreateController();

        var result = Assert.IsType<JsonResult>(await controller.Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;
        Assert.Empty(root.GetProperty("errors").EnumerateArray());
        Assert.Equal(2, root.GetProperty("fragments").GetArrayLength());
        Assert.All(root.GetProperty("fragments").EnumerateArray(), fragment =>
        {
            Assert.Equal(32, fragment.GetProperty("reservedPrefixUtf8Bytes").GetInt32());
            Assert.InRange(fragment.GetProperty("totalPayloadByteCount").GetInt32(), 0, 149);
        });
        Assert.Contains("RCON preview for CallOfDuty4", root.GetProperty("warnings")[0].GetString());
        Assert.Contains("another provider", root.GetProperty("warnings")[1].GetString());

        await controller.Preview(request, default);
        servers.Verify(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
            serverId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Preview_PreservesBudgetColourModeForSegmentationAndDisplay()
    {
        var serverId = Guid.NewGuid();
        const string message = "^1abcdefghijabcdefghij";
        var request = ServerRequest(serverId, message);
        var budget = new MessageChannelBudget(RuleMessageChannels.RconPrivateChat, 10) { UseCodColorCodes = false };
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                serverId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.OK,
                new ApiResponse<RuleMessageChannelBudgetDto>(new RuleMessageChannelBudgetDto
                {
                    Status = RuleMessageBudgetStatus.Available,
                    Budget = budget,
                    Constraints = new RconMessageCharacterConstraintsDto()
                })));

        var result = Assert.IsType<JsonResult>(await CreateController().Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.GetProperty("useCodColorCodes").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
        var fragments = json.RootElement.GetProperty("fragments").EnumerateArray()
            .Select(fragment => fragment.GetProperty("text").GetString()).ToArray();
        Assert.Equal(MessagePreparation.Prepare(message, [], budget).Fragments.Select(fragment => fragment.Text), fragments);
        Assert.Equal(message, string.Concat(fragments));
    }

    [Fact]
    public async Task Preview_RejectsRconLocalizationInsertsBeforeProducingFragments()
    {
        var serverId = Guid.NewGuid();
        var request = ServerRequest(serverId, "hello &&1");
        var budget = new RuleMessageChannelBudgetDto
        {
            Status = RuleMessageBudgetStatus.Available,
            Budget = new MessageChannelBudget(RuleMessageChannels.RconPrivateChat, 149),
            Constraints = new RconMessageCharacterConstraintsDto { DisallowLocalizationInserts = true }
        };
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                serverId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.OK, new ApiResponse<RuleMessageChannelBudgetDto>(budget)));

        var result = Assert.IsType<JsonResult>(await CreateController().Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("fragments").EnumerateArray());
    }

    [Fact]
    public async Task Preview_WhenRconBudgetIsUnavailable_PreservesBudgetWarningWithoutEstimateDisclaimer()
    {
        var serverId = Guid.NewGuid();
        var request = ServerRequest(serverId, "hello");
        var budget = new RuleMessageChannelBudgetDto { Status = RuleMessageBudgetStatus.Unavailable, Detail = "Prefix unavailable." };
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                serverId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.OK, new ApiResponse<RuleMessageChannelBudgetDto>(budget)));

        var result = Assert.IsType<JsonResult>(await CreateController().Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var warning = Assert.Single(json.RootElement.GetProperty("warnings").EnumerateArray());
        Assert.Contains("Prefix unavailable", warning.GetString());
        Assert.Single(json.RootElement.GetProperty("fragments").EnumerateArray());
    }

    [Fact]
    public async Task Preview_PublicMessage_UsesPublicRconChannel()
    {
        var serverId = Guid.NewGuid();
        var request = ServerRequest(serverId, "hello", "message.public");
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                serverId, RuleMessageChannels.RconPublicChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.ServiceUnavailable));

        var result = Assert.IsType<JsonResult>(await CreateController().Preview(request, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var warning = Assert.Single(json.RootElement.GetProperty("warnings").EnumerateArray());
        Assert.Contains("RCON budget for CallOfDuty4 is unavailable", warning.GetString());
        Assert.Single(json.RootElement.GetProperty("fragments").EnumerateArray());
        servers.Verify(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
            serverId, RuleMessageChannels.RconPublicChat, It.IsAny<CancellationToken>()), Times.Once);
    }
}
