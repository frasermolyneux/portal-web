using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface IGameServerSettingsService
{
    IReadOnlyCollection<string> DeletedNamespaces { get; }

    void PopulateConfigFromNamespace(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateDetailsViewData(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger);

    void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        bool needsFileTransportPassword,
        bool needsFileTransportHostKeyFingerprint,
        bool needsRconPassword,
        ILogger logger);

    IReadOnlyList<(string Namespace, string Configuration)> BuildNamespaceConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots);
}