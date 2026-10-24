using Microsoft.Extensions.Caching.Memory;

namespace XtremeIdiots.Portal.Web.Services;

public enum OptionalDataState
{
    Available,
    Unavailable,
    Stale
}

public sealed record OptionalOverviewData<T>(T? Data, OptionalDataState State, DateTimeOffset? ObservedAt = null)
    where T : class;

internal static class OptionalOverviewCache
{
    public async static Task<OptionalOverviewData<T>> ReadAsync<T>(
        IMemoryCache cache, DependencyReadCoordinator coordinator, string key,
        Func<CancellationToken, Task<T>> read, ILogger logger, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cache.TryGetValue<OptionalOverviewData<T>>(key, out var cached) && cached is not null
            ? cached
            : await coordinator.RunAsync(DependencyWorkload.Telemetry, key, async () =>
        {
            if (cache.TryGetValue<OptionalOverviewData<T>>(key, out var current) && current is not null)
                return current;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                var data = await read(timeout.Token).ConfigureAwait(false);
                var result = new OptionalOverviewData<T>(data, OptionalDataState.Available, DateTimeOffset.UtcNow);
                cache.Set(key + ":last-good", result, TimeSpan.FromMinutes(3));
                cache.Set(key, result, TimeSpan.FromSeconds(30));
                return result;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Optional overview dependency {DependencyKey} is unavailable", key);
                var result = cache.TryGetValue<OptionalOverviewData<T>>(key + ":last-good", out var previous) && previous is not null
                    ? previous with { State = OptionalDataState.Stale }
                    : new OptionalOverviewData<T>(null, OptionalDataState.Unavailable);
                cache.Set(key, result, TimeSpan.FromSeconds(5));
                return result;
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
