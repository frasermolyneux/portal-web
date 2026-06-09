using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public interface INamespaceSettingsSerializer
{
    IReadOnlyCollection<string> DeletedNamespaces { get; }

    IReadOnlyList<(string Namespace, string Configuration)> BuildGlobalSettingsConfigurations(GlobalSettingsViewModel model);

    IReadOnlyList<(string Namespace, string Configuration)> BuildGameServerConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots);
}