using MX.GeoLocation.Abstractions.Models.V1_1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed record IpIntelligenceResult(IpIntelligenceDto? Intelligence, string Status, DateTime ExpiresAtUtc)
{
    public bool HasRiskAssessment => Intelligence?.ProxyCheckStatus == SourceStatus.Success && Intelligence.ProxyCheck is not null;
    public int? RiskScore => HasRiskAssessment ? Intelligence!.ProxyCheck!.RiskScore : null;
    public bool? IsProxy => HasRiskAssessment ? Intelligence!.ProxyCheck!.IsProxy : null;
    public bool? IsVpn => HasRiskAssessment ? Intelligence!.ProxyCheck!.IsVpn : null;
    public string? ProxyType => HasRiskAssessment ? Intelligence!.ProxyCheck!.ProxyType : null;
}

public interface IIpIntelligenceService
{
    Task<IpIntelligenceResult> GetAsync(string? ipAddress, CancellationToken cancellationToken = default);
}
