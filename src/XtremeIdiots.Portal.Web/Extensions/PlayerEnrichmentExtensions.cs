using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class PlayerEnrichmentExtensions
{
    public async static Task<PlayerIntelligenceData> GetIntelligenceDataAsync(
        this PlayerDto playerDto,
        IIpIntelligenceService intelligenceService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (playerDto is null || string.IsNullOrEmpty(playerDto.IpAddress))
            return new PlayerIntelligenceData();

        try
        {
            var result = await intelligenceService.GetAsync(playerDto.IpAddress, cancellationToken).ConfigureAwait(false);
            return new PlayerIntelligenceData
            {
                CountryCode = result.Intelligence?.CountryCode ?? string.Empty,
                ProxyCheckRiskScore = result.RiskScore,
                IsProxy = result.IsProxy,
                IsVpn = result.IsVpn,
                ProxyType = result.ProxyType,
                Status = result.Status,
                ExpiresAtUtc = result.ExpiresAtUtc
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to enrich player DTO with intelligence data for IP {IpAddress}", playerDto.IpAddress);
        }

        return new PlayerIntelligenceData();
    }

    public async static Task<Dictionary<Guid, PlayerIntelligenceData>> GetIntelligenceDataAsync(
        this IEnumerable<PlayerDto> playerDtos,
        IIpIntelligenceService intelligenceService,
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
            var data = await player.GetIntelligenceDataAsync(intelligenceService, logger, ct).ConfigureAwait(false);
            bag[player.PlayerId] = data;
        }).ConfigureAwait(false);

        return new Dictionary<Guid, PlayerIntelligenceData>(bag);
    }
}
