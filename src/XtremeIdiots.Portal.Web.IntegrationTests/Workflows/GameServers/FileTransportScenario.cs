using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

internal sealed class FileTransportScenario
{
    public FileTransportScenario(bool upsertSucceeds = true, bool existingFingerprint = true)
    {
        GameServerId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        GameServer = CreateGameServer(GameServerId);
        SftpConfiguration = CreateConfiguration(
            "sftp",
            JsonConvert.SerializeObject(new
            {
                hostname = "sftp.example.com",
                port = 22,
                username = "ops-user",
                password = "CurrentSftpPassword",
                mapsRootPath = "/maps",
                hostKeyFingerprint = existingFingerprint ? "aa:bb:cc" : null,
            }));

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        SettingsService = new Mock<IGameServerSettingsService>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()))
            .Callback<EditGameServerDto, CancellationToken>((dto, _) =>
            {
                UpdatedGameServers.Enqueue(dto);
                Operations.Enqueue("UpdateGameServer");
            })
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), It.IsAny<GameServerFilter?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<GameServerOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([GameServer]))));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([SftpConfiguration]))));
        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.UpsertConfiguration(GameServerId, It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, UpsertConfigurationDto, CancellationToken>((_, ns, dto, _) =>
            {
                UpsertedConfigurations.Enqueue(new ConfigurationCommand(ns, dto.Configuration));
                Operations.Enqueue("UpsertConfiguration");
            })
            .ReturnsAsync(new ApiResult(upsertSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError));
        Mock.Get(RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([]))));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.GetTags(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(HttpStatusCode.OK, new ApiResponse<CollectionModel<TagDto>>(new CollectionModel<TagDto>([]))));

        SettingsService.SetupGet(service => service.DeletedNamespaces).Returns([]);
        SettingsService.Setup(service => service.PopulateConfigFromNamespace(It.IsAny<GameServerEditViewModel>(), It.IsAny<ConfigurationDto>(), It.IsAny<ILogger>()))
            .Callback<GameServerEditViewModel, ConfigurationDto, ILogger>((model, config, _) => PopulateCredentials(model, config));
        SettingsService.Setup(service => service.PopulateExistingCredentials(It.IsAny<GameServerEditViewModel>(), It.IsAny<string>(), It.IsAny<ConfigurationDto>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<ILogger>()))
            .Callback<GameServerEditViewModel, string, ConfigurationDto, bool, bool, bool, ILogger>((model, _, config, needsPassword, needsFingerprint, _, _) => PreserveExistingSecrets(model, config, needsPassword, needsFingerprint));
        SettingsService.Setup(service => service.BuildNamespaceConfigurations(It.IsAny<GameServerEditViewModel>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns<GameServerEditViewModel, bool, bool, bool>((model, canEdit, _, _) => canEdit ? [BuildConfiguration(model)] : []);
    }

    public GameServerDto GameServer { get; }
    public Guid GameServerId { get; }
    public Mock<IRepositoryApiClient> RepositoryClient { get; }
    public Mock<IGameServerSettingsService> SettingsService { get; }
    public ConfigurationDto SftpConfiguration { get; }
    public ConcurrentQueue<string> Operations { get; } = new();
    public ConcurrentQueue<EditGameServerDto> UpdatedGameServers { get; } = new();
    public ConcurrentQueue<ConfigurationCommand> UpsertedConfigurations { get; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IGameServerSettingsService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(SettingsService.Object);
    }

    private static (string Namespace, string Configuration) BuildConfiguration(GameServerEditViewModel model)
    {
        var ns = model.GameServer.FileTransportType == FileTransportType.Sftp ? "sftp" : "ftp";
        var document = new
        {
            hostname = model.FtpConfigHostname,
            port = model.FtpConfigPort,
            username = model.FtpConfigUsername,
            password = model.FtpConfigPassword,
            mapsRootPath = model.FtpConfigMapsRootPath,
            hostKeyFingerprint = ns == "sftp" ? model.FtpConfigHostKeyFingerprint : null,
        };
        return (ns, JsonConvert.SerializeObject(document));
    }

    private static ConfigurationDto CreateConfiguration(string ns, string configuration)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new { Namespace = ns, Configuration = configuration, LastModifiedUtc = DateTime.UtcNow }))!;
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "SFTP CoD4 Server",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.3",
            QueryPort = 28962,
            AgentEnabled = false,
            FileTransportEnabled = true,
            FileTransportType = FileTransportType.Sftp.ToString(),
            FtpEnabled = true,
            RconEnabled = false,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 3,
        }))!;
    }

    private static void PopulateCredentials(GameServerEditViewModel model, ConfigurationDto config, bool password = true, bool fingerprint = true)
    {
        if (!string.Equals(config.Namespace, "sftp", StringComparison.OrdinalIgnoreCase))
            return;
        model.FtpConfigHostname = "sftp.example.com";
        model.FtpConfigPort = 22;
        model.FtpConfigUsername = "ops-user";
        if (password)
            model.FtpConfigPassword = "CurrentSftpPassword";
        if (fingerprint)
            model.FtpConfigHostKeyFingerprint = "aa:bb:cc";
        model.FtpConfigMapsRootPath = "/maps";
    }

    private static void PreserveExistingSecrets(GameServerEditViewModel model, ConfigurationDto config, bool password, bool fingerprint)
    {
        if (!string.Equals(config.Namespace, "sftp", StringComparison.OrdinalIgnoreCase))
            return;
        using var document = JsonDocument.Parse(config.Configuration);
        if (password)
            model.FtpConfigPassword = document.RootElement.GetProperty("password").GetString();
        if (fingerprint && document.RootElement.TryGetProperty("hostKeyFingerprint", out var value))
            model.FtpConfigHostKeyFingerprint = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    public sealed record ConfigurationCommand(string Namespace, string Configuration);
}
