using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface INamespaceSettingsParser
{
    void PopulateGlobalSettingsViewModel(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerSettingsViewModel(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerDetails(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger);

    void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        bool needsFileTransportPassword,
        bool needsFileTransportHostKeyFingerprint,
        bool needsRconPassword,
        ILogger logger);
}