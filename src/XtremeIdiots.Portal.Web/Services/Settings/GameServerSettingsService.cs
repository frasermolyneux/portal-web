using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class GameServerSettingsService(
    INamespaceSettingsParser namespaceSettingsParser,
    INamespaceSettingsSerializer namespaceSettingsSerializer) : IGameServerSettingsService
{
    public IReadOnlyCollection<string> DeletedNamespaces => namespaceSettingsSerializer.DeletedNamespaces;

    public bool TryValidateVpnProtectionSettings(ConfigurationDto config, out IReadOnlyList<string> errors)
    {
        return namespaceSettingsParser.TryParseVpnProtectionServerSettings(config, out _, out errors);
    }

    public bool TryValidateGlobalVpnProtectionSettings(ConfigurationDto config, out IReadOnlyList<string> errors)
    {
        return namespaceSettingsParser.TryParseVpnProtectionSettings(config, out _, out errors);
    }

    public bool TryValidateSiegeModeSettings(ConfigurationDto config, out IReadOnlyList<string> errors)
    {
        return namespaceSettingsParser.TryParseSiegeModeServerSettings(config, out _, out errors);
    }

    public bool TryValidateGlobalSiegeModeSettings(ConfigurationDto config, out IReadOnlyList<string> errors)
    {
        return namespaceSettingsParser.TryParseSiegeModeSettings(config, out _, out errors);
    }

    public void PopulateConfigFromNamespace(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        namespaceSettingsParser.PopulateGameServerSettingsViewModel(model, config, logger);
    }

    public void PopulateGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        namespaceSettingsParser.PopulateGameServerGlobalDefaults(model, config, logger);
    }

    public void PopulateDetailsViewData(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger)
    {
        namespaceSettingsParser.PopulateGameServerDetails(viewData, fileTransportType, config, logger);
    }

    public void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        CredentialPreservationOptions options,
        ILogger logger)
    {
        namespaceSettingsParser.PopulateExistingCredentials(
            model,
            activeTransportNamespace,
            config,
            options,
            logger);
    }

    public IReadOnlyList<(string Namespace, string Configuration)> BuildNamespaceConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots)
    {
        return namespaceSettingsSerializer.BuildGameServerConfigurations(model, canEditFileTransport, canEditRcon, canConfigureScreenshots);
    }
}
