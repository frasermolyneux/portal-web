using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MX.Api.Abstractions;
using System.Net;
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

    private RulesPreviewController CreateController(bool allowed = true)
    {
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AuthPolicies.GlobalSettings_Admin))
            .ReturnsAsync(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return new RulesPreviewController(repository.Object, authorization.Object)
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

    [Fact]
    public async Task Preview_RequiresGlobalSettingsPermission()
    {
        var result = await CreateController(false).Preview(Request("""{"message":"hello"}"""), default);
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
    }

    [Fact]
    public async Task Preview_UsesSharedRendererAndFragmentationForOptedInListOnly()
    {
        var request = Request("""{"message":"literal {context.player.Name}","messages":["hello {context.player.Name}","next"]}""", "messages");
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
        Assert.Contains("interim assumption", root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Preview_ReportsInvalidTokensWithoutProducingFragments()
    {
        var result = Assert.IsType<JsonResult>(await CreateController().Preview(
            Request("""{"message":"hello {unknown.token}"}""", "message"), default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("fragments").EnumerateArray());
    }

    [Fact]
    public async Task Preview_RejectsTemplateOptInForUnsupportedParameter()
    {
        var result = await CreateController().Preview(Request("""{"message":"hello"}""", "recipientTags"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
