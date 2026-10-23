namespace XtremeIdiots.Portal.Web.Services;

public enum DependencyWorkload
{
    PlayerProfile,
    IpIntelligence,
    Telemetry,
    Configuration,
    MapMetadata
}

public sealed class DependencyReadCoordinator : IDisposable
{
    private readonly Dictionary<DependencyWorkload, SemaphoreSlim> limits = new()
    {
        [DependencyWorkload.PlayerProfile] = new(1),
        [DependencyWorkload.IpIntelligence] = new(4),
        [DependencyWorkload.Telemetry] = new(2),
        [DependencyWorkload.Configuration] = new(4),
        [DependencyWorkload.MapMetadata] = new(4)
    };

    // Fixed stripes bound coordination memory even when inputs contain many unique keys.
    private readonly Dictionary<DependencyWorkload, SemaphoreSlim[]> keyLocks = Enum.GetValues<DependencyWorkload>()
        .ToDictionary(workload => workload, _ => Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1)).ToArray());

    public async Task<T> RunAsync<T>(
        DependencyWorkload workload,
        string key,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var workloadLocks = keyLocks[workload];
        var keyLock = workloadLocks[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)workloadLocks.Length];
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var limit = limits[workload];
            await limit.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                limit.Release();
            }
        }
        finally
        {
            keyLock.Release();
        }
    }

    public void Dispose()
    {
        foreach (var limit in limits.Values)
            limit.Dispose();
        foreach (var workloadLocks in keyLocks.Values)
            foreach (var keyLock in workloadLocks)
                keyLock.Dispose();
    }
}
