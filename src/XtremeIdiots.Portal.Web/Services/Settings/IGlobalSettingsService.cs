using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface IGlobalSettingsService
{
    IReadOnlyCollection<string> DeletedNamespaces { get; }

    void PopulateModelFromNamespace(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger);

    IReadOnlyList<(string Namespace, string Configuration)> BuildNamespaceConfigurations(GlobalSettingsViewModel model);
}