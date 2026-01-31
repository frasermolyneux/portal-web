// Merged enrichment extensions (proxy + geo) moved from ProxyCheckExtensions.cs
// Original file retained temporarily during refactor; this is the canonical version.
using MX.GeoLocation.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class PlayerEnrichmentExtensions
{
    public async static Task<PlayerDto> EnrichWithProxyCheckDataAsync(
        this PlayerDto playerDto,
        IProxyCheckService proxyCheckService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDto is null)
            return playerDto!;

        if (string.IsNullOrEmpty(playerDto.IpAddress))
            return playerDto;

        try
        {
            var proxyCheckResult = await proxyCheckService.GetIpRiskDataAsync(playerDto.IpAddress, cancellationToken).ConfigureAwait(false);
            if (!proxyCheckResult.IsError)
            {
                playerDto.SetProxyCheckRiskScore(proxyCheckResult.RiskScore);
                playerDto.SetIsProxy(proxyCheckResult.IsProxy);
                playerDto.SetIsVpn(proxyCheckResult.IsVpn);
                playerDto.SetProxyType(proxyCheckResult.Type);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enrich player DTO with ProxyCheck data for IP {IpAddress}", playerDto.IpAddress);
        }

        return playerDto;
    }

    public async static Task<IEnumerable<PlayerDto>> EnrichWithProxyCheckDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IProxyCheckService proxyCheckService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDtos is null)
            return [];

        var players = playerDtos.ToList();

        // Use Parallel.ForEachAsync for concurrent enrichment of independent player records
        await Parallel.ForEachAsync(players.Where(p => p != null), cancellationToken, async (player, ct) =>
            await player.EnrichWithProxyCheckDataAsync(proxyCheckService, logger, ct).ConfigureAwait(false))
            .ConfigureAwait(false);

        return players;
    }

    public async static Task<PlayerDto> EnrichWithGeoLocationDataAsync(
        this PlayerDto playerDto,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDto is null || string.IsNullOrWhiteSpace(playerDto.IpAddress))
            return playerDto!;

        try
        {
            var geoResult = await geoLocationClient.GeoLookup.V1.GetGeoLocation(playerDto.IpAddress, cancellationToken).ConfigureAwait(false);
            if (geoResult.IsSuccess && geoResult.Result?.Data is not null && !string.IsNullOrWhiteSpace(geoResult.Result.Data.CountryCode))
            {
                playerDto.SetCountryCode(geoResult.Result.Data.CountryCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enrich player DTO with GeoLocation data for IP {IpAddress}", playerDto.IpAddress);
        }
        return playerDto;
    }

    public async static Task<IEnumerable<PlayerDto>> EnrichWithGeoLocationDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDtos is null)
            return [];

        var players = playerDtos.ToList();

        // Use Parallel.ForEachAsync for concurrent enrichment of independent player records
        await Parallel.ForEachAsync(players.Where(p => p != null), cancellationToken, async (player, ct) =>
            await player.EnrichWithGeoLocationDataAsync(geoLocationClient, logger, ct).ConfigureAwait(false))
            .ConfigureAwait(false);

        return players;
    }

    public async static Task<IEnumerable<PlayerDto>> EnrichWithPlayerDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IProxyCheckService proxyCheckService,
        IGeoLocationApiClient geoLocationClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var proxyEnriched = await playerDtos.EnrichWithProxyCheckDataAsync(proxyCheckService, logger, cancellationToken).ConfigureAwait(false);
        return await proxyEnriched.EnrichWithGeoLocationDataAsync(geoLocationClient, logger, cancellationToken).ConfigureAwait(false);
    }
}