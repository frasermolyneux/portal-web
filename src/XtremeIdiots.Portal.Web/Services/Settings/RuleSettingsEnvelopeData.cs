using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public static class RuleSettingsEnvelopeData
{
    public static void CopyFromAuthoritative(GlobalSettingsViewModel source, GlobalSettingsViewModel destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        destination.VpnProtection.EnvelopeExtensionData =
            RuleSettingsJson.CloneExtensionData(source.VpnProtection.EnvelopeExtensionData);
        destination.SiegeMode.EnvelopeExtensionData =
            RuleSettingsJson.CloneExtensionData(source.SiegeMode.EnvelopeExtensionData);
    }

    public static void CopyFromAuthoritative(GameServerEditViewModel source, GameServerEditViewModel destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        destination.VpnProtection.EnvelopeExtensionData =
            RuleSettingsJson.CloneExtensionData(source.VpnProtection.EnvelopeExtensionData);
        destination.SiegeMode.EnvelopeExtensionData =
            RuleSettingsJson.CloneExtensionData(source.SiegeMode.EnvelopeExtensionData);
    }
}
