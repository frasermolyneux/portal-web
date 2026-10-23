using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class MapMetadataServiceTests
{
    [Fact]
    public async Task Reads_DeduplicateIdsWithoutRetainingStaleMapsAcrossCalls()
    {
        var mapId = Guid.NewGuid();
        var before = BuildMap(mapId, "mp_before");
        var after = BuildMap(mapId, "mp_after");
        var client = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        client.SetupSequence(x => x.Maps.V1.GetMap(mapId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapDto>(HttpStatusCode.OK, new ApiResponse<MapDto>(before)))
            .ReturnsAsync(new ApiResult<MapDto>(HttpStatusCode.OK, new ApiResponse<MapDto>(after)));
        using var coordinator = new DependencyReadCoordinator();
        var service = new MapMetadataService(client.Object, coordinator, Mock.Of<ILogger<MapMetadataService>>());

        Assert.Same(before, Assert.Single(await service.GetAsync([mapId, mapId])).Value);
        Assert.Same(after, Assert.Single(await service.GetAsync([mapId])).Value);
        client.Verify(x => x.Maps.V1.GetMap(mapId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static MapDto BuildMap(Guid id, string name)
    {
        return JsonConvert.DeserializeObject<MapDto>(JsonConvert.SerializeObject(new { MapId = id, MapName = name }))!;
    }
}
