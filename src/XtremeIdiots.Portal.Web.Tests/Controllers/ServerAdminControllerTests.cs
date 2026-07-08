using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
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
using System.Text.Json;
using System.Text.Json.Serialization;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ChatMessages;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class ServerAdminControllerTests
{
    private readonly static JsonSerializerOptions cod4xPluginJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IServersApiClient> mockServersApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IGeoLocationApiClient> mockGeoLocationClient = new();
    private readonly Mock<IAdminActionTopics> mockAdminActionTopics = new();
    private readonly Mock<IAgentTelemetryService> mockAgentTelemetryService = new();
    private readonly IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());
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
            memoryCache,
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
            .Setup(x => x.CoD4xRcon.V1.Say(
                serverId,
                It.Is<CoD4xMessageRequestDto>(r => r.Message == longMessage),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.OK, new ApiResponse<string>("ok")));

        var sut = CreateSut();

        var result = await sut.SendSayCommand(serverId, inputMessage, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.Say(
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

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.Say(
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
    public async Task RequestCod4xPluginOperation_Install_WhenValid_QueuesOperationAndPreservesRuntimeState()
    {
        var serverId = Guid.NewGuid();
        var existingCod4xConfigJson = /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "enabled": true,
              "pluginRootDirectory": "/plugins",
              "runtimeState": {
                "currentVersion": "1.2.3",
                "previousKnownGoodVersion": "1.2.2",
                "lastOperationId": "prev-op",
                "lastOperationStatus": "Succeeded",
                "lastOperationUtc": "2026-01-01T12:00:00Z",
                "lastError": null
              }
            }
            """;

        var existingCod4xConfiguration = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = Cod4xPluginSettingsConstants.Namespace,
            Configuration = existingCod4xConfigJson,
            LastModifiedUtc = DateTime.UtcNow
        }))!;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x, GameServerPlatform.Windows))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.GetConfigurations(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([existingCod4xConfiguration]))));

        UpsertConfigurationDto? capturedUpsertDto = null;
        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                serverId,
                Cod4xPluginSettingsConstants.Namespace,
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, UpsertConfigurationDto, CancellationToken>((_, _, dto, _) => capturedUpsertDto = dto)
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        mockConfiguration
            .Setup(x => x["CoD4xPluginLifecycle:ArtifactsStorageAccountName"])
            .Returns("invalid account");

        mockConfiguration
            .Setup(x => x["CoD4xPluginLifecycle:ArtifactsContainerName"])
            .Returns("invalid container");

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            Cod4xPluginOperationAction.Install,
            "1.2.4",
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        Assert.NotNull(capturedUpsertDto);
        Assert.NotNull(capturedUpsertDto!.Configuration);

        var serializedDocument = SystemTextJsonSerializer.Deserialize<Cod4xPluginSettingsDocument>(
            capturedUpsertDto.Configuration,
            cod4xPluginJsonOptions);

        Assert.NotNull(serializedDocument);
        Assert.NotNull(serializedDocument!.OperationRequest);
        Assert.Equal(Cod4xPluginOperationAction.Install, serializedDocument.OperationRequest!.Action);
        Assert.Equal("1.2.4", serializedDocument.OperationRequest.TargetVersion);
        Assert.NotNull(serializedDocument.OperationRequest.ExtensionData);
        Assert.True(serializedDocument.OperationRequest.ExtensionData!.TryGetValue("artifactPath", out var artifactPathElement));
        Assert.Equal(JsonValueKind.String, artifactPathElement.ValueKind);
        Assert.Contains("1.2.4", artifactPathElement.GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("portal-cod4x-plugin", artifactPathElement.GetString(), StringComparison.OrdinalIgnoreCase);
        var artifactPath = artifactPathElement.GetString()!.Replace('\\', '/');
        Assert.Contains("/releases/1.2.4/", artifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/windows/", artifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".dll", artifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(serializedDocument.OperationRequest.ExtensionData.ContainsKey("artifactPathFallback"));
        Assert.True(serializedDocument.OperationRequest.ExtensionData.TryGetValue("artifactBlobPath", out var artifactBlobPathElement));
        Assert.Equal(JsonValueKind.String, artifactBlobPathElement.ValueKind);
        var artifactBlobPath = artifactBlobPathElement.GetString()!;
        Assert.Contains("/releases/1.2.4/", $"/{artifactBlobPath}", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/windows/", $"/{artifactBlobPath}", StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".dll", artifactBlobPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(serializedDocument.OperationRequest.ExtensionData.TryGetValue("artifactStorageAccountName", out var artifactStorageAccountNameElement));
        Assert.Equal(JsonValueKind.String, artifactStorageAccountNameElement.ValueKind);
        Assert.True(serializedDocument.OperationRequest.ExtensionData.TryGetValue("artifactContainerName", out var artifactContainerNameElement));
        Assert.Equal(JsonValueKind.String, artifactContainerNameElement.ValueKind);

        Assert.NotNull(serializedDocument.RuntimeState);
        Assert.Equal("1.2.3", serializedDocument.RuntimeState!.CurrentVersion);
        Assert.Equal("1.2.2", serializedDocument.RuntimeState.PreviousKnownGoodVersion);
    }

    [Fact]
    public async Task RequestCod4xPluginOperation_WhenExistingConfigContainsLegacyBooleanString_QueuesOperation()
    {
        var serverId = Guid.NewGuid();
        var existingCod4xConfigJson = /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "enabled": "true",
              "pluginRootDirectory": "/plugins",
              "runtimeState": {
                "currentVersion": "1.2.3",
                "lastOperationId": "prev-op",
                "lastOperationStatus": "Succeeded"
              }
            }
            """;

        var existingCod4xConfiguration = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = Cod4xPluginSettingsConstants.Namespace,
            Configuration = existingCod4xConfigJson,
            LastModifiedUtc = DateTime.UtcNow
        }))!;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x, GameServerPlatform.Windows))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.GetConfigurations(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([existingCod4xConfiguration]))));

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                serverId,
                Cod4xPluginSettingsConstants.Namespace,
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            Cod4xPluginOperationAction.Install,
            "1.2.4",
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));
    }

    [Fact]
    public async Task RequestCod4xPluginOperation_Install_WithUnsupportedPlatform_ReturnsFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(
                CreateGameServerDto(serverId, GameType.CallOfDuty4x, GameServerPlatform.Unknown))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            Cod4xPluginOperationAction.Install,
            "1.2.4",
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));

        Assert.False(payload.Value<bool>("success"));
        Assert.Contains("not supported", payload.Value<string>("message"), StringComparison.OrdinalIgnoreCase);

        mockRepositoryApiClient.Verify(x => x.GameServerConfigurations.V1.UpsertConfiguration(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<UpsertConfigurationDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TryResolveCod4xArtifactPlatform_WhenRequestedPlatformMissing_ReturnsFalse()
    {
        var resolved = ServerAdminController.TryResolveCod4xArtifactPlatform(
            GameServerPlatform.Windows,
            hasWindowsArtifacts: false,
            hasLinuxArtifacts: true,
            out var resolvedPlatform);

        Assert.False(resolved);
        Assert.Equal(GameServerPlatform.Unknown, resolvedPlatform);
    }

    [Fact]
    public void TryResolveCod4xArtifactPlatform_WhenRequestedPlatformPresent_ReturnsRequestedPlatform()
    {
        var resolved = ServerAdminController.TryResolveCod4xArtifactPlatform(
            GameServerPlatform.Linux,
            hasWindowsArtifacts: true,
            hasLinuxArtifacts: true,
            out var resolvedPlatform);

        Assert.True(resolved);
        Assert.Equal(GameServerPlatform.Linux, resolvedPlatform);
    }

    [Fact]
    public async Task RequestCod4xPluginOperation_Install_WithInvalidVersion_ReturnsValidationFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            Cod4xPluginOperationAction.Install,
            "1.2.4 bad",
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));

        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("Target version contains invalid characters.", payload.Value<string>("message"));

        mockRepositoryApiClient.Verify(x => x.GameServerConfigurations.V1.UpsertConfiguration(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<UpsertConfigurationDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestCod4xPluginOperation_WithInvalidActionEnum_ReturnsValidationFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            (Cod4xPluginOperationAction)999,
            null,
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));

        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("A valid CoD4x plugin operation is required.", payload.Value<string>("message"));

        mockRepositoryApiClient.Verify(x => x.GameServerConfigurations.V1.UpsertConfiguration(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<UpsertConfigurationDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestCod4xPluginOperation_ForNonCod4xServer_ReturnsFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4))));

        var sut = CreateSut();

        var result = await sut.RequestCod4xPluginOperation(
            serverId,
            Cod4xPluginOperationAction.Rollback,
            null,
            CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));

        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("CoD4x plugin lifecycle operations are only supported for CoD4x servers.", payload.Value<string>("message"));
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

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xPermBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xPermBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KickRconPlayer_Cod4_UsesGameScopedKickEndpoint()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod4Rcon.V1.Kick(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.OK, new ApiResponse<string>("ok")));

        var sut = CreateSut();

        var result = await sut.KickRconPlayer(serverId, 2, "", "PlayerThree", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.Cod4Rcon.V1.Kick(
            serverId,
            It.Is<ClientSlotRequest>(r => r.ClientId == 2),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.ClientKick(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KickRconPlayer_Cod4_WhenRconFails_ReturnsFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod4Rcon.V1.Kick(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        var result = await sut.KickRconPlayer(serverId, 2, string.Empty, "PlayerThree", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("RconFailed", payload.Value<string>("error"));
    }

    [Fact]
    public async Task TempBanRconPlayer_Cod2_UsesGameScopedTempBanEndpointWithServerDefaultDuration()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty2))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod2Rcon.V1.TempBan(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.OK, new ApiResponse<string>("ok")));

        var sut = CreateSut();

        var result = await sut.TempBanRconPlayer(serverId, 4, string.Empty, "PlayerFour", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));
        Assert.Contains("server default", payload.Value<string>("message") ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        mockServersApiClient.Verify(x => x.Cod2Rcon.V1.TempBan(
            serverId,
            It.Is<ClientSlotRequest>(r => r.ClientId == 4),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xTempBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TempBanRconPlayer_Cod2_WhenRconFails_ReturnsFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty2))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod2Rcon.V1.TempBan(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        var result = await sut.TempBanRconPlayer(serverId, 4, string.Empty, "PlayerFour", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("RconFailed", payload.Value<string>("error"));
    }

    [Fact]
    public async Task BanRconPlayer_Cod5_UsesGameScopedBanEndpoint()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty5))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod5Rcon.V1.Ban(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.OK, new ApiResponse<string>("ok")));

        var sut = CreateSut();

        var result = await sut.BanRconPlayer(serverId, 7, string.Empty, "PlayerFive", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.Cod5Rcon.V1.Ban(
            serverId,
            It.Is<ClientSlotRequest>(r => r.ClientId == 7),
            It.IsAny<CancellationToken>()), Times.Once);

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xPermBanRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.BanClient(
            It.IsAny<Guid>(),
            It.IsAny<CoD4xClientReasonRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BanRconPlayer_Cod5_WhenRconFails_ReturnsFailure()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty5))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.Cod5Rcon.V1.Ban(
                serverId,
                It.IsAny<ClientSlotRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<string>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        var result = await sut.BanRconPlayer(serverId, 7, string.Empty, "PlayerFive", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));
        Assert.Equal("RconFailed", payload.Value<string>("error"));
    }

    [Fact]
    public async Task GetMapRotation_Cod4x_UsesCod4xMapsEndpoint()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.GetMaps(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RconMapCollectionDto>(
                HttpStatusCode.OK,
                new ApiResponse<RconMapCollectionDto>(new RconMapCollectionDto([
                    new RconMapDto("war", "mp_crash")
                ]))));

        mockRepositoryApiClient
            .Setup(x => x.Maps.V1.GetMaps(
                It.IsAny<GameType?>(),
                It.IsAny<string[]?>(),
                It.IsAny<MapsFilter?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<MapsOrder?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([]))));

        var sut = CreateSut();

        var result = await sut.GetMapRotation(serverId, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.True(payload.Value<bool>("success"));

        mockServersApiClient.Verify(x => x.CoD4xRcon.V1.GetMaps(serverId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMapRotation_Cod4x_WhenMapsRequestFails_ReturnsFailurePayload()
    {
        var serverId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServerDto(serverId, GameType.CallOfDuty4x))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        mockServersApiClient
            .Setup(x => x.CoD4xRcon.V1.GetMaps(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RconMapCollectionDto>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        var result = await sut.GetMapRotation(serverId, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(jsonResult.Value));
        Assert.False(payload.Value<bool>("success"));

        var maps = payload["maps"] as JArray;
        Assert.NotNull(maps);
        Assert.Empty(maps);
    }

    private static GameServerDto CreateGameServerDto(
        Guid gameServerId,
        GameType gameType = GameType.CallOfDuty4,
        GameServerPlatform platform = GameServerPlatform.Windows)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "Test Server",
            GameType = gameType,
            Platform = platform,
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
