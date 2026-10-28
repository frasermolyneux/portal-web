using Microsoft.Extensions.Caching.Memory;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class MapExperienceService(
    IRepositoryApiClient repository, IMemoryCache cache, DependencyReadCoordinator coordinator)
{
    public Task<PublicServerRotationsDto> Rotations(CancellationToken cancellationToken)
    {
        return Cached("maps:public-rotations", async ct =>
            {
                var result = await repository.MapRotations.V1.GetPublicServerRotations(ct).ConfigureAwait(false);
                return result.IsSuccess && result.Result?.Data is { } data
                    ? data : throw new InvalidOperationException("Public map rotations are unavailable.");
            }, cancellationToken);
    }

    public Task<MapInsightsDto> Insights(int days, GameType? family, Guid? server, CancellationToken cancellationToken)
    {
        var to = DateTime.UtcNow;
        return Cached($"maps:insights:{days}:{family}:{server}:{to:yyyyMMddHH}", async ct =>
        {
            var result = await repository.MapAnalytics.V1.GetInsights(to.AddDays(-days), to, family, server, ct).ConfigureAwait(false);
            return result.IsSuccess && result.Result?.Data is { } data
                ? data : throw new InvalidOperationException("Map insights are unavailable.");
        }, cancellationToken);
    }

    private Task<T> Cached<T>(string key, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken) where T : class
    {
        return coordinator.RunAsync(DependencyWorkload.MapMetadata, key, async () =>
            {
                if (cache.TryGetValue<T>(key, out var cached) && cached is not null)
                    return cached;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var result = await read(timeout.Token).ConfigureAwait(false);
                cache.Set(key, result, TimeSpan.FromSeconds(30));
                return result;
            }, cancellationToken);
    }
}
