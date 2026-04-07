namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Holds per-player intelligence enrichment data (proxy/VPN/geo) for a single request.
/// Replaces the former static ConcurrentDictionary-based cache to avoid memory leaks
/// and stale data across requests.
/// </summary>
public record PlayerIntelligenceData
{
    public string CountryCode { get; init; } = string.Empty;
    public int ProxyCheckRiskScore { get; init; }
    public bool IsProxy { get; init; }
    public bool IsVpn { get; init; }
    public string ProxyType { get; init; } = string.Empty;
}