using System.Collections.Concurrent;

using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Services;

public interface IMapMetadataService
{
    Task<IReadOnlyDictionary<Guid, MapDto>> GetAsync(IEnumerable<Guid> mapIds, CancellationToken cancellationToken = default);
}

public sealed class MapMetadataService(
    IRepositoryApiClient repositoryApiClient,
    DependencyReadCoordinator coordinator,
    ILogger<MapMetadataService> logger) : IMapMetadataService
{
    public async Task<IReadOnlyDictionary<Guid, MapDto>> GetAsync(IEnumerable<Guid> mapIds, CancellationToken cancellationToken = default)
    {
        var maps = new ConcurrentDictionary<Guid, MapDto>();
        await Parallel.ForEachAsync(mapIds.Distinct(), new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (id, ct) =>
        {
            var response = await coordinator.RunAsync(DependencyWorkload.MapMetadata, id.ToString(),
                () => repositoryApiClient.Maps.V1.GetMap(id, ct), ct).ConfigureAwait(false);
            if (response.IsSuccess && response.Result?.Data is { } map)
                maps[id] = map;
            else
                logger.LogWarning("Map metadata unavailable for map {MapId}, status {StatusCode}", id, response.StatusCode);
        }).ConfigureAwait(false);
        return maps;
    }
}
