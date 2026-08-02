using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

internal sealed class Cod4xPluginLifecycleScenario
{
    private readonly static JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Cod4xPluginLifecycleScenario(
        bool upsertSucceeds = true,
        bool configurationLoadSucceeds = true,
        bool malformedConfiguration = false,
        bool pendingRequest = false,
        int upsertDelayMilliseconds = 0)
    {
        GameServerId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        GameServer = CreateGameServer(GameServerId);
        CurrentConfiguration = CreateConfiguration(malformedConfiguration ? "{ malformed" : CreateExistingConfigurationJson(pendingRequest));
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        AgentTelemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => configurationLoadSucceeds
                ? new ApiResult<CollectionModel<ConfigurationDto>>(
                    HttpStatusCode.OK,
                    new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([CurrentConfiguration])))
                : new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.InternalServerError));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.UpsertConfiguration(
                GameServerId,
                Cod4xPluginSettingsConstants.Namespace,
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, UpsertConfigurationDto, CancellationToken>((_, _, dto, _) =>
            {
                AttemptedConfigurations.Enqueue(dto.Configuration);
                if (upsertSucceeds)
                    CurrentConfiguration = CreateConfiguration(dto.Configuration);
            })
            .Returns(async () =>
            {
                if (upsertDelayMilliseconds > 0)
                    await Task.Delay(upsertDelayMilliseconds);
                return new ApiResult(upsertSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            });
        AgentTelemetry
            .Setup(service => service.GetServerStatusAsync(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentServerStatus { ActivityStatus = AgentActivityStatus.Offline });
    }

    public Mock<IAgentTelemetryService> AgentTelemetry { get; }
    public ConcurrentQueue<string> AttemptedConfigurations { get; } = new();
    public ConfigurationDto CurrentConfiguration { get; private set; }
    public GameServerDto GameServer { get; }
    public Guid GameServerId { get; }
    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IAgentTelemetryService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(AgentTelemetry.Object);
    }

    public Cod4xPluginSettingsDocument GetAttemptedDocument()
    {
        return System.Text.Json.JsonSerializer.Deserialize<Cod4xPluginSettingsDocument>(
            Assert.Single(AttemptedConfigurations),
            jsonOptions) ?? throw new InvalidOperationException("Queued lifecycle document was empty.");
    }

    private static string CreateExistingConfigurationJson(bool pendingRequest)
    {
        return System.Text.Json.JsonSerializer.Serialize(new Cod4xPluginSettingsDocument
        {
            SchemaVersion = Cod4xPluginSettingsConstants.SchemaVersion,
            Enabled = true,
            PluginRootDirectory = "/plugins",
            RuntimeState = new Cod4xPluginRuntimeState
            {
                CurrentVersion = "1.2.3",
                PreviousKnownGoodVersion = "1.2.2",
                LastOperationId = "previous-operation",
                LastOperationStatus = Cod4xPluginOperationStatus.Succeeded,
                LastOperationUtc = DateTimeOffset.Parse("2026-01-01T12:00:00Z"),
            },
            OperationRequest = pendingRequest
                ? new Cod4xPluginOperationRequest
                {
                    OperationId = "pending-operation",
                    Action = Cod4xPluginOperationAction.Rollback,
                    RequestedAtUtc = DateTimeOffset.Parse("2026-01-02T12:00:00Z"),
                    RequestedBy = "Existing Operator",
                }
                : null,
        }, jsonOptions);
    }

    private static ConfigurationDto CreateConfiguration(string configuration)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = Cod4xPluginSettingsConstants.Namespace,
            Configuration = configuration,
            LastModifiedUtc = DateTime.UtcNow,
        }))!;
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "Lifecycle CoD4x Server",
            GameType = GameType.CallOfDuty4x.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.9",
            QueryPort = 28967,
            AgentEnabled = true,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 8,
        }))!;
    }
}
