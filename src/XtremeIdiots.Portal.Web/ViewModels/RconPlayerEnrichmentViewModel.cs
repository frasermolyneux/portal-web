namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed record RconPlayerEnrichmentViewModel(
    Guid? PlayerId,
    string? Username,
    string ProfileStatus,
    string? CountryCode,
    int? ProxyCheckRiskScore,
    bool? IsProxy,
    bool? IsVpn,
    string? ProxyType,
    string IpIntelligenceStatus,
    DateTime ExpiresAtUtc);
