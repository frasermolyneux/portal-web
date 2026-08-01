using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

internal sealed class GameServerRconScenario
{
    public GameServerRconScenario(bool rconUpsertSucceeds = true)
    {
        GameServerId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        GameServer = CreateGameServer(GameServerId);
        RconConfiguration = CreateConfiguration("rcon", /*lang=json,strict*/ "{\"password\":\"CurrentPassword\"}");

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };
        SettingsService = new Mock<IGameServerSettingsService>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()))
            .Callback<EditGameServerDto, CancellationToken>((dto, _) => UpdatedGameServers.Enqueue(dto))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(
                It.IsAny<GameType[]?>(),
                It.IsAny<Guid[]?>(),
                It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerOrder>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([GameServer]))));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([RconConfiguration]))));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.UpsertConfiguration(
                GameServerId,
                "rcon",
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, UpsertConfigurationDto, CancellationToken>((_, ns, dto, _) =>
                UpsertedConfigurations.Enqueue(new ConfigurationCommand(ns, dto.Configuration)))
            .ReturnsAsync(new ApiResult(rconUpsertSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError));
        Mock.Get(RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([]))));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.GetTags(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<TagDto>>(new CollectionModel<TagDto>([]))));

        SettingsService.SetupGet(service => service.DeletedNamespaces).Returns([]);
        SettingsService
            .Setup(service => service.PopulateConfigFromNamespace(
                It.IsAny<GameServerEditViewModel>(),
                It.IsAny<ConfigurationDto>(),
                It.IsAny<ILogger>()))
            .Callback<GameServerEditViewModel, ConfigurationDto, ILogger>((model, config, _) =>
            {
                if (string.Equals(config.Namespace, "rcon", StringComparison.OrdinalIgnoreCase))
                    model.RconConfigPassword = "CurrentPassword";
            });
        SettingsService
            .Setup(service => service.PopulateExistingCredentials(
                It.IsAny<GameServerEditViewModel>(),
                It.IsAny<string>(),
                It.IsAny<ConfigurationDto>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<ILogger>()))
            .Callback<GameServerEditViewModel, string, ConfigurationDto, bool, bool, bool, ILogger>((model, _, config, _, _, needsRconPassword, _) =>
            {
                if (needsRconPassword && string.Equals(config.Namespace, "rcon", StringComparison.OrdinalIgnoreCase))
                    model.RconConfigPassword = "CurrentPassword";
            });
        SettingsService
            .Setup(service => service.BuildNamespaceConfigurations(
                It.IsAny<GameServerEditViewModel>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Returns<GameServerEditViewModel, bool, bool, bool>((model, _, canEditRcon, _) =>
                canEditRcon
                    ? [("rcon", JsonConvert.SerializeObject(new { password = model.RconConfigPassword }))]
                    : []);
    }

    public GameServerDto GameServer { get; }

    public Guid GameServerId { get; }

    public ConfigurationDto RconConfiguration { get; }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public Mock<IGameServerSettingsService> SettingsService { get; }

    public ConcurrentQueue<EditGameServerDto> UpdatedGameServers { get; } = new();

    public ConcurrentQueue<ConfigurationCommand> UpsertedConfigurations { get; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IGameServerSettingsService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(SettingsService.Object);
    }

    private static ConfigurationDto CreateConfiguration(string ns, string configuration)
    {
        var json = JsonConvert.SerializeObject(new
        {
            Namespace = ns,
            Configuration = configuration,
            LastModifiedUtc = DateTime.UtcNow,
        });

        return JsonConvert.DeserializeObject<ConfigurationDto>(json)!;
    }

    private static GameServerDto CreateGameServer(Guid gameServerId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "CoD4 Server 1",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Windows.ToString(),
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            FtpEnabled = false,
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1,
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }

    public sealed record ConfigurationCommand(string Namespace, string Configuration);
}
