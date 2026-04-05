using MX.GeoLocation.Abstractions.Models.V1_1;
using MX.GeoLocation.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class PlayerEnrichmentExtensions
{
    public async static Task<PlayerDto> EnrichWithIntelligenceDataAsync(
        this PlayerDto playerDto,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDto is null)
            return playerDto!;

        if (string.IsNullOrEmpty(playerDto.IpAddress))
            return playerDto;

        try
        {
            var result = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(playerDto.IpAddress, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Result?.Data is not null)
            {
                var intelligence = result.Result.Data;

                if (!string.IsNullOrWhiteSpace(intelligence.CountryCode))
                {
                    playerDto.SetCountryCode(intelligence.CountryCode);
                }

                if (intelligence.ProxyCheckStatus == SourceStatus.Success && intelligence.ProxyCheck is not null)
                {
                    playerDto.SetProxyCheckRiskScore(intelligence.ProxyCheck.RiskScore);
                    playerDto.SetIsProxy(intelligence.ProxyCheck.IsProxy);
                    playerDto.SetIsVpn(intelligence.ProxyCheck.IsVpn);
                    playerDto.SetProxyType(intelligence.ProxyCheck.ProxyType);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enrich player DTO with intelligence data for IP {IpAddress}", playerDto.IpAddress);
        }

        return playerDto;
    }

    public async static Task<IEnumerable<PlayerDto>> EnrichWithIntelligenceDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDtos is null)
            return [];

        var players = playerDtos.ToList();

        await Parallel.ForEachAsync(players.Where(p => p != null), cancellationToken, async (player, ct) =>
            await player.EnrichWithIntelligenceDataAsync(geoLocationClient, logger, ct).ConfigureAwait(false))
            .ConfigureAwait(false);

        return players;
    }
}