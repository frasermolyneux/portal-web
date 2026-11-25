using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface IGameServerSettingsService
{
    IReadOnlyCollection<string> DeletedNamespaces { get; }

    bool TryValidateVpnProtectionSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    bool TryValidateGlobalVpnProtectionSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    bool TryValidateWelcomeRulesSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    bool TryValidateGlobalWelcomeRulesSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    bool TryValidateSiegeModeSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    bool TryValidateGlobalSiegeModeSettings(ConfigurationDto config, out IReadOnlyList<string> errors);

    void PopulateConfigFromNamespace(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateDetailsViewData(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger);

    void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        CredentialPreservationOptions options,
        ILogger logger);

    IReadOnlyList<(string Namespace, string Configuration)> BuildNamespaceConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots);
}
