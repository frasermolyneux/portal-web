using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using PlayersApiController = XtremeIdiots.Portal.Web.ApiControllers.PlayersController;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class PlayersControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly Mock<IGeoLocationApiClient> mockGeoLocationApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<PlayersApiController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private PlayersApiController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new PlayersApiController(
            mockRepositoryApiClient.Object,
            mockGeoLocationApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetPlayersAjax_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream();

        // Act
        var result = await sut.GetPlayersAjax(null, null, null, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetPlayersAjax_ForwardsSelectedTagAndRequestsTagsEntityOptions()
    {
        // Arrange
        var selectedTagId = Guid.NewGuid();
        var player = CreatePlayerDtoWithTag();
        var apiResponse = new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([player]))
        {
            Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 0, top: 25)
        };

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayers(
                GameType.CallOfDuty4,
                PlayersFilter.Tag,
                selectedTagId.ToString(),
                0,
                25,
                PlayersOrder.LastSeenDesc,
                PlayerEntityOptions.Tags))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK, apiResponse));

        var request = new
        {
            draw = 1,
            start = 0,
            length = 25,
            columns = new[]
            {
                new { data = "username", name = "username", searchable = true, orderable = true, search = new { value = "", regex = false } },
                new { data = "tags", name = "tags", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "ipAddress", name = "ipAddress", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "guid", name = "guid", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "firstSeen", name = "firstSeen", searchable = false, orderable = true, search = new { value = "", regex = false } },
                new { data = "lastSeen", name = "lastSeen", searchable = false, orderable = true, search = new { value = "", regex = false } }
            },
            order = new[] { new { column = 5, dir = "desc" } },
            search = new { value = "", regex = false }
        };

        var body = JsonConvert.SerializeObject(request);
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        // Act
        var result = await sut.GetPlayersAjax(GameType.CallOfDuty4, PlayersFilter.UsernameAndGuid, selectedTagId, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(ok.Value));
        var data = Assert.IsType<JArray>(payload["data"]);

        Assert.Single(data);
        Assert.Equal("Trusted", data[0]?["tags"]?[0]?["name"]?.Value<string>());

        mockRepositoryApiClient.Verify(x => x.Players.V1.GetPlayers(
            GameType.CallOfDuty4,
            PlayersFilter.Tag,
            selectedTagId.ToString(),
            0,
            25,
            PlayersOrder.LastSeenDesc,
            PlayerEntityOptions.Tags), Times.Once);
    }

    [Fact]
    public async Task GetPlayersAjax_DefaultsFilter_WhenPlayersFilterNotProvided()
    {
        // Arrange
        var apiResponse = new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([]))
        {
            Pagination = new ApiPagination(totalCount: 0, filteredCount: 0, skip: 0, top: 25)
        };

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayers(
                null,
                PlayersFilter.UsernameAndGuid,
                string.Empty,
                0,
                25,
                PlayersOrder.LastSeenDesc,
                PlayerEntityOptions.Tags))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK, apiResponse));

        var request = new
        {
            draw = 2,
            start = 0,
            length = 25,
            columns = new[]
            {
                new { data = "username", name = "username", searchable = true, orderable = true, search = new { value = "", regex = false } },
                new { data = "tags", name = "tags", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "ipAddress", name = "ipAddress", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "guid", name = "guid", searchable = true, orderable = false, search = new { value = "", regex = false } },
                new { data = "firstSeen", name = "firstSeen", searchable = false, orderable = true, search = new { value = "", regex = false } },
                new { data = "lastSeen", name = "lastSeen", searchable = false, orderable = true, search = new { value = "", regex = false } }
            },
            order = new[] { new { column = 5, dir = "desc" } },
            search = new { value = "", regex = false }
        };

        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request)));

        // Act
        var result = await sut.GetPlayersAjax(null, null, null, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        mockRepositoryApiClient.Verify(x => x.Players.V1.GetPlayers(
            null,
            PlayersFilter.UsernameAndGuid,
            string.Empty,
            0,
            25,
            PlayersOrder.LastSeenDesc,
            PlayerEntityOptions.Tags), Times.Once);
    }

    private static PlayerDto CreatePlayerDtoWithTag()
    {
        var json = JsonConvert.SerializeObject(new
        {
            PlayerId = Guid.NewGuid(),
            GameType = "CallOfDuty4",
            Username = "TestPlayer",
            Guid = "ABC-123",
            IpAddress = "",
            FirstSeen = DateTime.UtcNow.AddDays(-2),
            LastSeen = DateTime.UtcNow,
            Tags = new[]
            {
                new
                {
                    PlayerTagId = Guid.NewGuid(),
                    PlayerId = Guid.NewGuid(),
                    TagId = Guid.NewGuid(),
                    Tag = new
                    {
                        TagId = Guid.NewGuid(),
                        Name = "Trusted",
                        TagHtml = "<span class=\"badge bg-success\">Trusted</span>",
                        UserDefined = true
                    }
                }
            }
        });

        return JsonConvert.DeserializeObject<PlayerDto>(json)!;
    }
}
