using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model representing a related player with enrichment (proxy + geo) data.
/// </summary>
public class RelatedPlayerEnrichedViewModel
{
    public Guid PlayerId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public int GameType { get; set; }

    // Fields from enriched RelatedPlayerDto
    public DateTime LastSeen { get; set; }
    public bool HasActiveBan { get; set; }
    public int AdminActionCount { get; set; }
    public string LinkingIpAddress { get; set; } = string.Empty;
    public DateTime LinkingIpLastUsedByPlayer { get; set; }
    public DateTime LinkingIpLastUsedByRelated { get; set; }
    public bool IsCurrentIp { get; set; }
    public int SharedIpCount { get; set; }

    // Geo enrichment
    public int? RiskScore { get; set; }
    public bool? IsProxy { get; set; }
    public bool? IsVpn { get; set; }
    public string? ProxyType { get; set; }
    public string? CountryCode { get; set; }

    public static RelatedPlayerEnrichedViewModel FromRelatedPlayerDto(RelatedPlayerDto dto)
    {
        return new RelatedPlayerEnrichedViewModel
        {
            PlayerId = dto.PlayerId,
            Username = dto.Username,
            IpAddress = dto.IpAddress,
            GameType = (int)dto.GameType,
            LastSeen = dto.LastSeen,
            HasActiveBan = dto.HasActiveBan,
            AdminActionCount = dto.AdminActionCount,
            LinkingIpAddress = dto.LinkingIpAddress,
            LinkingIpLastUsedByPlayer = dto.LinkingIpLastUsedByPlayer,
            LinkingIpLastUsedByRelated = dto.LinkingIpLastUsedByRelated,
            IsCurrentIp = dto.IsCurrentIp,
            SharedIpCount = dto.SharedIpCount
        };
    }
}
