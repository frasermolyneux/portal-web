using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class GlobalSettingsService(
    INamespaceSettingsParser namespaceSettingsParser,
    INamespaceSettingsSerializer namespaceSettingsSerializer) : IGlobalSettingsService
{
    public IReadOnlyCollection<string> DeletedNamespaces => namespaceSettingsSerializer.DeletedNamespaces;

    public void PopulateModelFromNamespace(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger)
    {
        namespaceSettingsParser.PopulateGlobalSettingsViewModel(model, config, logger);
    }

    public bool TryValidateSiegeModeSettings(ConfigurationDto config, out IReadOnlyList<string> errors)
    {
        return namespaceSettingsParser.TryParseSiegeModeSettings(config, out _, out errors);
    }

    public IReadOnlyList<(string Namespace, string Configuration)> BuildNamespaceConfigurations(GlobalSettingsViewModel model)
    {
        return namespaceSettingsSerializer.BuildGlobalSettingsConfigurations(model);
    }
}
