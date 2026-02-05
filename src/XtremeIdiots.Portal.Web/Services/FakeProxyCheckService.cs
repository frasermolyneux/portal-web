namespace XtremeIdiots.Portal.Web.Services;

/// <summary>
/// Fake ProxyCheck service for UITest mode that returns fixed safe results
/// </summary>
public class FakeProxyCheckService : IProxyCheckService
{
    private readonly ILogger<FakeProxyCheckService> _logger;

    public FakeProxyCheckService(ILogger<FakeProxyCheckService> logger)
    {
        _logger = logger;
    }

    public Task<ProxyCheckResult> GetIpRiskDataAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FakeProxyCheckService returning safe result for IP {IpAddress}", ipAddress);

        var result = new ProxyCheckResult
        {
            IpAddress = ipAddress,
            IsError = false,
            IsProxy = false,
            IsVpn = false,
            Type = string.Empty,
            RiskScore = 0,
            Country = "US",
            Region = "California",
            AsNumber = "AS15169",
            AsOrganization = "Google LLC"
        };

        return Task.FromResult(result);
    }
}
