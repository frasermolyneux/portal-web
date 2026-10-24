using System.Collections.Concurrent;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed record FileTransportReadSettings(string? Hostname, int Port, string? Username, string? Password);

public sealed record GameServerReadSettings(
    string? RconPassword,
    string? HtmlBanner,
    FileTransportReadSettings Ftp,
    FileTransportReadSettings Sftp);

public interface IGameServerConfigurationReadService
{
    Task<Dictionary<Guid, GameServerReadSettings>> GetAsync(
        IEnumerable<Guid> serverIds, bool includeCredentials = false, CancellationToken cancellationToken = default);
}

public sealed class GameServerConfigurationReadService(
    IRepositoryApiClient repositoryApiClient,
    INamespaceSettingsParser parser,
    DependencyReadCoordinator coordinator,
    ILogger<GameServerConfigurationReadService> logger) : IGameServerConfigurationReadService
{
    public async Task<Dictionary<Guid, GameServerReadSettings>> GetAsync(
        IEnumerable<Guid> serverIds, bool includeCredentials = false, CancellationToken cancellationToken = default)
    {
        var result = new ConcurrentDictionary<Guid, GameServerReadSettings>();
        await Parallel.ForEachAsync(serverIds.Distinct(), new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (id, ct) =>
        {
            try
            {
                var settings = await coordinator.RunAsync(DependencyWorkload.Configuration, id.ToString(), async () =>
                {
                    var response = await repositoryApiClient.GameServerConfigurations.V1.GetConfigurations(id, ct).ConfigureAwait(false);
                    if (!response.IsSuccess || response.Result?.Data?.Items is null)
                    {
                        logger.LogWarning("Configuration read failed for server {ServerId}, status {StatusCode}", id, response.StatusCode);
                        return null;
                    }

                    var ftp = new GameServerEditViewModel { GameServer = new GameServerViewModel { FileTransportType = FileTransportType.Ftp } };
                    var sftp = new GameServerEditViewModel
                    {
                        GameServer = new GameServerViewModel { FileTransportType = FileTransportType.Sftp },
                        FileTransportConfigPort = 22
                    };
                    foreach (var configuration in response.Result.Data.Items)
                    {
                        if (configuration.Namespace is "ftp" or "rcon" or "serverlist")
                            parser.PopulateGameServerSettingsViewModel(ftp, configuration, logger);
                        else if (configuration.Namespace == "sftp")
                            parser.PopulateGameServerSettingsViewModel(sftp, configuration, logger);
                    }

                    return new GameServerReadSettings(
                        includeCredentials ? ftp.RconConfigPassword : null,
                        ftp.ServerListConfigHtmlBanner,
                        ProjectTransport(ftp, includeCredentials),
                        ProjectTransport(sftp, includeCredentials));
                }, ct).ConfigureAwait(false);
                if (settings is not null)
                    result[id] = settings;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to load configuration for server {ServerId}", id);
            }
        }).ConfigureAwait(false);
        return new Dictionary<Guid, GameServerReadSettings>(result);
    }

    private static FileTransportReadSettings ProjectTransport(GameServerEditViewModel model, bool includeCredentials)
    {
        return new(model.FileTransportConfigHostname, model.FileTransportConfigPort,
            includeCredentials ? model.FileTransportConfigUsername : null,
            includeCredentials ? model.FileTransportConfigPassword : null);
    }
}
