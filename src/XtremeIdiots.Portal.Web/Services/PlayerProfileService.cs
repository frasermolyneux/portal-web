using Microsoft.Extensions.Caching.Memory;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class PlayerProfileService(
    IRepositoryApiClient client,
    IMemoryCache cache,
    DependencyReadCoordinator coordinator,
    ILogger<PlayerProfileService> logger) : IPlayerProfileService
{
    public Task<PlayerProfileResult> GetAsync(GameType gameType, string? playerGuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(playerGuid))
            return Task.FromResult(new PlayerProfileResult(null, null, "notApplicable", DateTime.UtcNow.AddMinutes(10)));

        var key = $"shared-player-profile:{gameType}:{playerGuid.ToUpperInvariant()}";
        return cache.TryGetValue<PlayerProfileResult>(key, out var cached) && cached is not null
            ? Task.FromResult(cached)
            : coordinator.RunAsync(DependencyWorkload.PlayerProfile, key, async () =>
        {
            if (cache.TryGetValue<PlayerProfileResult>(key, out var existing) && existing is not null)
                return existing;

            PlayerProfileResult result;
            try
            {
                var response = await client.Players.V1.GetPlayerByGameType(gameType, playerGuid, PlayerEntityOptions.None).ConfigureAwait(false);
                result = response.IsSuccess && response.Result?.Data is { } player &&
                    player.GameType == gameType && string.Equals(player.Guid, playerGuid, StringComparison.OrdinalIgnoreCase)
                    ? new(player.PlayerId, player.Username, "available", DateTime.UtcNow.AddMinutes(10))
                    : response.IsNotFound
                    ? new(null, null, "notFound", DateTime.UtcNow.AddMinutes(1))
                    : new(null, null, "unavailable", DateTime.UtcNow.AddSeconds(30));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Player profile enrichment failed for game {GameType}", gameType);
                result = new(null, null, "unavailable", DateTime.UtcNow.AddSeconds(30));
            }

            return cache.Set(key, result, result.ExpiresAtUtc);
        }, cancellationToken);
    }
}
