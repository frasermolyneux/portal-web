using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;

namespace XtremeIdiots.Portal.Web.ViewModels;

public static class VpnProtectionRuleEditorOptions
{
    public static string GetSignalLabel(VpnProtectionSignal signal)
    {
        return signal switch
        {
            VpnProtectionSignal.ProxyCheckRiskScore => "ProxyCheck risk score",
            VpnProtectionSignal.ProxyCheckIsProxy => "ProxyCheck detects a proxy",
            VpnProtectionSignal.ProxyCheckIsVpn => "ProxyCheck detects a VPN",
            VpnProtectionSignal.ProxyCheckProxyType => "ProxyCheck proxy type",
            VpnProtectionSignal.ProxyCheckAsNumber => "ProxyCheck AS number",
            VpnProtectionSignal.ProxyCheckAsOrganization => "ProxyCheck AS organization",
            VpnProtectionSignal.MaxMindAnonymizerConfidence => "MaxMind anonymizer confidence",
            VpnProtectionSignal.MaxMindIsAnonymous => "MaxMind detects anonymous use",
            VpnProtectionSignal.MaxMindIsAnonymousVpn => "MaxMind detects an anonymous VPN",
            VpnProtectionSignal.MaxMindIsHostingProvider => "MaxMind detects a hosting provider",
            VpnProtectionSignal.MaxMindIsPublicProxy => "MaxMind detects a public proxy",
            VpnProtectionSignal.MaxMindIsResidentialProxy => "MaxMind detects a residential proxy",
            VpnProtectionSignal.MaxMindIsTorExitNode => "MaxMind detects a Tor exit node",
            VpnProtectionSignal.MaxMindProviderName => "MaxMind provider name",
            VpnProtectionSignal.MaxMindStatus => "MaxMind lookup status",
            VpnProtectionSignal.ProxyCheckStatus => "ProxyCheck lookup status",
            VpnProtectionSignal.IsPartial => "Combined result is partial",
            VpnProtectionSignal.Unknown => "Select a signal",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    public static string GetSignalDescription(VpnProtectionSignal signal)
    {
        return signal switch
        {
            VpnProtectionSignal.ProxyCheckRiskScore => "Risk score returned by ProxyCheck, from 0 (lowest) to 100 (highest).",
            VpnProtectionSignal.MaxMindAnonymizerConfidence => "MaxMind confidence that the address belongs to an anonymizer, from 0 to 100.",
            VpnProtectionSignal.ProxyCheckProxyType => "Proxy classification returned by ProxyCheck, compared without case sensitivity.",
            VpnProtectionSignal.ProxyCheckAsNumber => "Autonomous system number returned by ProxyCheck, for example AS12345.",
            VpnProtectionSignal.ProxyCheckAsOrganization => "Autonomous system organization returned by ProxyCheck.",
            VpnProtectionSignal.MaxMindProviderName => "Anonymizer provider name returned by MaxMind.",
            VpnProtectionSignal.MaxMindStatus => "Whether the MaxMind lookup succeeded, failed, or was unavailable.",
            VpnProtectionSignal.ProxyCheckStatus => "Whether the ProxyCheck lookup succeeded, failed, or was unavailable.",
            VpnProtectionSignal.IsPartial => "True when only one intelligence provider returned usable data.",
            VpnProtectionSignal.ProxyCheckIsProxy or
            VpnProtectionSignal.ProxyCheckIsVpn or
            VpnProtectionSignal.MaxMindIsAnonymous or
            VpnProtectionSignal.MaxMindIsAnonymousVpn or
            VpnProtectionSignal.MaxMindIsHostingProvider or
            VpnProtectionSignal.MaxMindIsPublicProxy or
            VpnProtectionSignal.MaxMindIsResidentialProxy or
            VpnProtectionSignal.MaxMindIsTorExitNode => "True or false signal returned by the selected intelligence provider.",
            VpnProtectionSignal.Unknown => "Choose the intelligence value this rule should inspect.",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    public static string GetValueKind(VpnProtectionSignal signal)
    {
        return signal switch
        {
            VpnProtectionSignal.ProxyCheckRiskScore or
            VpnProtectionSignal.MaxMindAnonymizerConfidence => "numeric",
            VpnProtectionSignal.ProxyCheckIsProxy or
            VpnProtectionSignal.ProxyCheckIsVpn or
            VpnProtectionSignal.MaxMindIsAnonymous or
            VpnProtectionSignal.MaxMindIsAnonymousVpn or
            VpnProtectionSignal.MaxMindIsHostingProvider or
            VpnProtectionSignal.MaxMindIsPublicProxy or
            VpnProtectionSignal.MaxMindIsResidentialProxy or
            VpnProtectionSignal.MaxMindIsTorExitNode or
            VpnProtectionSignal.IsPartial => "boolean",
            VpnProtectionSignal.MaxMindStatus or
            VpnProtectionSignal.ProxyCheckStatus => "status",
            VpnProtectionSignal.ProxyCheckProxyType or
            VpnProtectionSignal.ProxyCheckAsNumber or
            VpnProtectionSignal.ProxyCheckAsOrganization or
            VpnProtectionSignal.MaxMindProviderName or
            VpnProtectionSignal.Unknown => "string",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    public static string GetExpectedValueHint(VpnProtectionSignal signal)
    {
        return signal switch
        {
            VpnProtectionSignal.ProxyCheckIsProxy or
            VpnProtectionSignal.ProxyCheckIsVpn or
            VpnProtectionSignal.MaxMindIsAnonymous or
            VpnProtectionSignal.MaxMindIsAnonymousVpn or
            VpnProtectionSignal.MaxMindIsHostingProvider or
            VpnProtectionSignal.MaxMindIsPublicProxy or
            VpnProtectionSignal.MaxMindIsResidentialProxy or
            VpnProtectionSignal.MaxMindIsTorExitNode or
            VpnProtectionSignal.IsPartial => "Choose whether the signal must be true or false.",
            VpnProtectionSignal.ProxyCheckRiskScore or
            VpnProtectionSignal.MaxMindAnonymizerConfidence => $"Enter a whole number from {VpnProtectionSettingsConstants.MinNumericValue} to {VpnProtectionSettingsConstants.MaxNumericValue}.",
            VpnProtectionSignal.MaxMindStatus or
            VpnProtectionSignal.ProxyCheckStatus => "Choose Success, Failed, or Unavailable.",
            VpnProtectionSignal.ProxyCheckAsNumber => "Enter an AS number such as AS12345.",
            VpnProtectionSignal.ProxyCheckProxyType => "Enter all or part of the proxy type returned by ProxyCheck.",
            VpnProtectionSignal.ProxyCheckAsOrganization => "Enter all or part of the organization name.",
            VpnProtectionSignal.MaxMindProviderName => "Enter all or part of the provider name.",
            VpnProtectionSignal.Unknown => "Select a signal first.",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
    }

    public static string GetOperatorLabel(VpnProtectionComparisonOperator comparisonOperator)
    {
        return comparisonOperator switch
        {
            VpnProtectionComparisonOperator.Equal => "Is equal to",
            VpnProtectionComparisonOperator.NotEqual => "Is not equal to",
            VpnProtectionComparisonOperator.GreaterThan => "Is greater than",
            VpnProtectionComparisonOperator.GreaterThanOrEqual => "Is at least",
            VpnProtectionComparisonOperator.LessThan => "Is less than",
            VpnProtectionComparisonOperator.LessThanOrEqual => "Is at most",
            VpnProtectionComparisonOperator.Contains => "Contains",
            VpnProtectionComparisonOperator.Unknown => "Select an operator",
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator), comparisonOperator, null)
        };
    }

    public static string GetActionLabel(VpnProtectionAction action)
    {
        return action switch
        {
            VpnProtectionAction.Observation => "Observation (record only)",
            VpnProtectionAction.Kick => "Kick (disconnect player)",
            VpnProtectionAction.Ban => "Ban (ban and disconnect)",
            VpnProtectionAction.Unknown => "Select an action",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }
}