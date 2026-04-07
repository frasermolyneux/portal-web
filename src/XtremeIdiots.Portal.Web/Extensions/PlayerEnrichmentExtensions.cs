using MX.GeoLocation.Abstractions.Models.V1_1;
using MX.GeoLocation.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class PlayerEnrichmentExtensions
{
    public async static Task<PlayerIntelligenceData> GetIntelligenceDataAsync(
        this PlayerDto playerDto,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDto is null || string.IsNullOrEmpty(playerDto.IpAddress))
            return new PlayerIntelligenceData();

        try
        {
            var result = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(playerDto.IpAddress, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Result?.Data is not null)
            {
                var intelligence = result.Result.Data;
                var countryCode = !string.IsNullOrWhiteSpace(intelligence.CountryCode) ? intelligence.CountryCode : string.Empty;

                if (intelligence.ProxyCheckStatus == SourceStatus.Success && intelligence.ProxyCheck is not null)
                {
                    return new PlayerIntelligenceData
                    {
                        CountryCode = countryCode,
                        ProxyCheckRiskScore = intelligence.ProxyCheck.RiskScore,
                        IsProxy = intelligence.ProxyCheck.IsProxy,
                        IsVpn = intelligence.ProxyCheck.IsVpn,
                        ProxyType = intelligence.ProxyCheck.ProxyType
                    };
                }

                return new PlayerIntelligenceData { CountryCode = countryCode };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enrich player DTO with intelligence data for IP {IpAddress}", playerDto.IpAddress);
        }

        return new PlayerIntelligenceData();
    }

    public async static Task<Dictionary<Guid, PlayerIntelligenceData>> GetIntelligenceDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, PlayerIntelligenceData>();

        if (playerDtos is null)
            return result;

        var players = playerDtos.Where(p => p != null).ToList();
        var bag = new System.Collections.Concurrent.ConcurrentDictionary<Guid, PlayerIntelligenceData>();

        await Parallel.ForEachAsync(players, cancellationToken, async (player, ct) =>
        {
            var data = await player.GetIntelligenceDataAsync(geoLocationClient, logger, ct).ConfigureAwait(false);
            bag[player.PlayerId] = data;
        }).ConfigureAwait(false);

        return new Dictionary<Guid, PlayerIntelligenceData>(bag);
    }
}