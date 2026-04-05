using MX.GeoLocation.Abstractions.Models.V1_1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for displaying player IP address information with intelligence data
/// </summary>
public class PlayerIpAddressViewModel
{
    /// <summary>
    /// The IP address data transfer object
    /// </summary>
    public IpAddressDto IpAddressDto { get; set; } = null!;

    /// <summary>
    /// Aggregated IP intelligence data (geo + risk)
    /// </summary>
    public IpIntelligenceDto? Intelligence { get; set; }

    /// <summary>
    /// The IP address string
    /// </summary>
    public string Address => IpAddressDto?.Address ?? string.Empty;

    /// <summary>
    /// Indicates if this is the player's current IP address
    /// </summary>
    public bool IsCurrentIp { get; set; }

    /// <summary>
    /// Risk score from proxy check (0-100)
    /// </summary>
    public int RiskScore => Intelligence?.ProxyCheck?.RiskScore ?? 0;

    /// <summary>
    /// Indicates if the IP address is identified as a proxy
    /// </summary>
    public bool IsProxy => Intelligence?.ProxyCheck?.IsProxy ?? false;

    /// <summary>
    /// Indicates if the IP address is identified as a VPN
    /// </summary>
    public bool IsVpn => Intelligence?.ProxyCheck?.IsVpn ?? false;

    /// <summary>
    /// The type of proxy if identified
    /// </summary>
    public string ProxyType => Intelligence?.ProxyCheck?.ProxyType ?? string.Empty;

    /// <summary>
    /// The country code from intelligence data
    /// </summary>
    public string CountryCode => Intelligence?.CountryCode ?? "unknown";

    /// <summary>
    /// Gets the CSS class for displaying risk level
    /// </summary>
    public string RiskClass => IPAddressExtensions.GetRiskClass(RiskScore);
}