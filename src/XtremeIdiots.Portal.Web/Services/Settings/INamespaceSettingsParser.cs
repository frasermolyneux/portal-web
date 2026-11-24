using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface INamespaceSettingsParser
{
    bool TryParseVpnProtectionSettings(
        ConfigurationDto config,
        out VpnProtectionSettingsDocument? settings,
        out IReadOnlyList<string> errors);

    bool TryParseVpnProtectionServerSettings(
        ConfigurationDto config,
        out VpnProtectionServerSettingsDocument? settings,
        out IReadOnlyList<string> errors);

    bool TryParseSiegeModeServerSettings(
        ConfigurationDto config,
        out SiegeModeServerSettingsOverrideDocument? settings,
        out IReadOnlyList<string> errors);

    bool TryParseSiegeModeSettings(
        ConfigurationDto config,
        out SiegeModeSettingsDocument? settings,
        out IReadOnlyList<string> errors);

    void PopulateGlobalSettingsViewModel(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerSettingsViewModel(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger);

    void PopulateGameServerDetails(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger);

    void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        CredentialPreservationOptions options,
        ILogger logger);
}
