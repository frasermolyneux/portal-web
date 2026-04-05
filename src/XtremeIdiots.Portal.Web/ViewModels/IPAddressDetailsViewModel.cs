using MX.GeoLocation.Abstractions.Models.V1_1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for displaying detailed information about an IP address including geolocation, proxy detection, and associated players
/// </summary>
public class IPAddressDetailsViewModel
{
    /// <summary>
    /// Gets or sets the IP address being displayed
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the aggregated IP intelligence data (geo + risk)
    /// </summary>
    public IpIntelligenceDto? Intelligence { get; set; }

    /// <summary>
    /// Gets or sets the total count of players associated with this IP address
    /// </summary>
    public int TotalPlayersCount { get; set; }

    /// <summary>
    /// Gets or sets the collection of players associated with this IP address
    /// </summary>
    public IEnumerable<PlayerDto> Players { get; set; } = [];
}