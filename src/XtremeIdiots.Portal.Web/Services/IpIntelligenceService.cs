using Microsoft.Extensions.Caching.Memory;
using MX.GeoLocation.Api.Client.V1;
using System.Net;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class IpIntelligenceService(
    IGeoLocationApiClient client,
    IMemoryCache cache,
    DependencyReadCoordinator coordinator,
    ILogger<IpIntelligenceService> logger) : IIpIntelligenceService
{
    public Task<IpIntelligenceResult> GetAsync(string? ipAddress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var address))
            return Task.FromResult(new IpIntelligenceResult(null, "notApplicable", DateTime.UtcNow.AddMinutes(1)));

        var normalized = address.ToString();
        var key = $"shared-ip-intelligence:{normalized}";
        return cache.TryGetValue<IpIntelligenceResult>(key, out var cached) && cached is not null
            ? Task.FromResult(cached)
            : coordinator.RunAsync(DependencyWorkload.IpIntelligence, normalized, async () =>
        {
            if (cache.TryGetValue<IpIntelligenceResult>(key, out var existing) && existing is not null)
                return existing;

            IpIntelligenceResult result;
            try
            {
                var response = await client.GeoLookup.V1_1.GetIpIntelligence(normalized, cancellationToken).ConfigureAwait(false);
                var intelligence = response.IsSuccess ? response.Result?.Data : null;
                result = new(intelligence, "unavailable", DateTime.UtcNow.AddSeconds(30));
                if (result.HasRiskAssessment)
                    result = result with { Status = "available" };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "IP intelligence enrichment failed");
                result = new(null, "unavailable", DateTime.UtcNow.AddSeconds(30));
            }

            // A short coordination snapshot deduplicates concurrent misses. The API client owns its
            // existing successful-response cache; do not independently extend its 15-minute lifetime.
            return cache.Set(key, result, result.ExpiresAtUtc);
        }, cancellationToken);
    }
}
