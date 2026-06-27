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
using System.Security.Claims;
using System.Text;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class MapRotationsApiControllerTests
{
    private readonly Mock<IRepositoryApiClient> repositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<MapRotationsApiController>> logger = new();
    private readonly Mock<IConfiguration> configuration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private MapRotationsApiController CreateSut(string requestBody, ClaimsPrincipal? user = null)
    {
        var controller = new MapRotationsApiController(
            repositoryApiClient.Object,
            telemetryClient,
            logger.Object,
            configuration.Object,
            auditLogger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user ?? new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(UserProfileClaimType.XtremeIdiotsId, "12345")
                    ], "TestAuth"))
                }
            }
        };

        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

        return controller;
    }

    [Fact]
    public async Task GetMapRotationsAjax_WhenRotationIsPublished_ReturnsPublishedStatusToken()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var rotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty4,
            "Rotation A",
            "desc",
            "tdm",
            version: 1,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            mapRotationMaps: [],
            serverAssignments: [])
        {
            Status = MapRotationStatus.Published
        };

        repositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotations(
                It.IsAny<GameType[]?>(),
                It.IsAny<string?>(),
                It.IsAny<MapRotationStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<MapRotationsFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<MapRotationsOrder?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationDto>>(new CollectionModel<MapRotationDto>([rotation]))));

        var requestBody = JsonConvert.SerializeObject(new
        {
            draw = 1,
            start = 0,
            length = 10,
            columns = new[]
            {
                new
                {
                    data = "title",
                    name = "title",
                    searchable = "true",
                    orderable = "true",
                    search = new
                    {
                        value = string.Empty,
                        regex = "false"
                    }
                },
                new
                {
                    data = "status",
                    name = "status",
                    searchable = "true",
                    orderable = "true",
                    search = new
                    {
                        value = string.Empty,
                        regex = "false"
                    }
                }
            },
            search = new
            {
                value = string.Empty,
                regex = "false"
            },
            order = new[]
            {
                new
                {
                    column = 0,
                    dir = "asc"
                }
            }
        });

        var sut = CreateSut(requestBody);

        // Act
        var result = await sut.GetMapRotationsAjax(null);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = JObject.FromObject(ok.Value!);
        var status = payload["data"]?[0]?["status"]?.Value<string>();

        Assert.Equal("Published", status);
    }
}
