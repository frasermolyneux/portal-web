using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

[Trait("Category", "Unit")]
public class MapsControllerTests
{
    [Theory]
    [InlineData(GameType.CallOfDuty4)]
    [InlineData(GameType.CallOfDuty4x)]
    public async Task MapCatalogue_StockMapsHaveBuiltInBadgesInsteadOfRedirectLinks(GameType gameType)
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        string[] names = ["mp_bog", "mp_custom"];
        var maps = names.Select(name => JsonConvert.DeserializeObject<MapDto>(
            JsonConvert.SerializeObject(new { MapId = Guid.NewGuid(), GameType = gameType, MapName = name }))!).ToList();
        repository.Setup(x => x.Maps.V1.GetMaps(gameType, null, null, null, 0, 10, MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>(maps))));
        var controller = new MapsController(repository.Object, new TelemetryClient(new TelemetryConfiguration()),
            Mock.Of<ILogger<MapsController>>(), Mock.Of<IConfiguration>(), Mock.Of<IAuditLogger>());
        await using var body = new MemoryStream(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"draw":1,"start":0,"length":10}"""));
        var context = new DefaultHttpContext();
        context.Request.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var response = Assert.IsType<OkObjectResult>(await controller.GetMapListAjax(gameType));
        var rows = Assert.IsType<JArray>(JObject.FromObject(response.Value!)["data"]);

        Assert.True(rows[0]["builtIn"]!.Value<bool>());
        Assert.Equal(JTokenType.Null, rows[0]["redirectManagerUrl"]!.Type);
        Assert.False(rows[1]["builtIn"]!.Value<bool>());
        Assert.Equal("https://maps.xtremeidiots.com/?game=cod4&map=mp_custom", rows[1]["redirectManagerUrl"]!.Value<string>());
    }
}
