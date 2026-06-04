using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using ConnectedPlayersApiController = XtremeIdiots.Portal.Web.ApiControllers.ConnectedPlayersController;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class ConnectedPlayersControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<ConnectedPlayersApiController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private ConnectedPlayersApiController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new ConnectedPlayersApiController(
            mockRepositoryApiClient.Object,
            memoryCache,
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

    private static ClaimsPrincipal CreateSeniorAdminUser(Guid? userProfileId = null)
    {
        var claims = new List<Claim>
        {
            new(UserProfileClaimType.SeniorAdmin, "true")
        };

        if (userProfileId.HasValue)
        {
            claims.Add(new Claim(UserProfileClaimType.UserProfileId, userProfileId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task GetConnectedPlayersAjax_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream();

        // Act
        var result = await sut.GetConnectedPlayersAjax();

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetConnectedPlayersAjax_WithValidBody_ReturnsDataTablePayload()
    {
        // Arrange
        var item = CreateConnectedPlayerDto();
        var apiResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(new CollectionModel<ConnectedPlayerDto>([item]))
        {
            Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 0, top: 500)
        };

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, apiResponse));

        var request = new
        {
            draw = 3,
            start = 0,
            length = 25,
            columns = new[]
            {
                new { data = "gameType", name = "gameType", searchable = true, orderable = true, search = new { value = "", regex = false } },
                new { data = "username", name = "username", searchable = true, orderable = true, search = new { value = "", regex = false } },
                new { data = "linkedAtUtc", name = "linkedAtUtc", searchable = false, orderable = true, search = new { value = "", regex = false } }
            },
            order = new[] { new { column = 2, dir = "desc" } },
            search = new { value = "", regex = false }
        };

        var body = JsonConvert.SerializeObject(request);
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        // Act
        var result = await sut.GetConnectedPlayersAjax();

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = JObject.Parse(JsonConvert.SerializeObject(ok.Value));

        Assert.Equal(3, (payload["draw"] ?? payload["Draw"])?.Value<int>());
        Assert.Equal(1, payload["recordsTotal"]?.Value<int>());
        Assert.Equal(1, payload["recordsFiltered"]?.Value<int>());
        Assert.Equal(1, payload["data"]?.Count());
    }

    [Fact]
    public async Task GetConnectedPlayersAjax_WithInvalidOrderColumnIndex_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            draw = 1,
            start = 0,
            length = 25,
            columns = new[]
            {
                new { data = "gameType", name = "gameType", searchable = true, orderable = true, search = new { value = "", regex = false } }
            },
            order = new[] { new { column = 5, dir = "desc" } },
            search = new { value = "", regex = false }
        };

        var body = JsonConvert.SerializeObject(request);
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        // Act
        var result = await sut.GetConnectedPlayersAjax();

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.GetConnectedPlayers(
            It.IsAny<Guid?>(),
            It.IsAny<Guid?>(),
            It.IsAny<Repository.Abstractions.Constants.V1.GameType?>(),
            It.IsAny<bool?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchPlayers_WithoutGameType_ReturnsEmptyArray()
    {
        // Arrange
        var sut = CreateSut(CreateSeniorAdminUser());

        // Act
        var result = await sut.SearchPlayers("Player", null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<Array>(okResult.Value);
        Assert.Empty(payload);

        mockRepositoryApiClient.Verify(x => x.Players.V1.GetPlayers(
            It.IsAny<GameType?>(),
            It.IsAny<PlayersFilter?>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<PlayersOrder?>(),
            It.IsAny<PlayerEntityOptions>()), Times.Never);
    }

    [Fact]
    public async Task SearchUserProfiles_ReturnsOnlyProfilesWithForumId()
    {
        // Arrange
        var withForumId = CreateUserProfileDto("AdminOne", "12345", "admin1@example.com");
        var withoutForumId = CreateUserProfileDto("AdminTwo", null, "admin2@example.com");

        var response = new ApiResponse<CollectionModel<UserProfileDto>>(new CollectionModel<UserProfileDto>([withForumId, withoutForumId]))
        {
            Pagination = new ApiPagination(totalCount: 2, filteredCount: 2, skip: 0, top: 20)
        };

        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfiles("Admin", null, 0, 20, UserProfilesOrder.DisplayNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<UserProfileDto>>(HttpStatusCode.OK, response));

        var sut = CreateSut(CreateSeniorAdminUser());

        // Act
        var result = await sut.SearchUserProfiles("Admin");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = JArray.Parse(JsonConvert.SerializeObject(okResult.Value));

        Assert.Single(payload);
        Assert.Equal(withForumId.UserProfileId.ToString(), payload[0]?["id"]?.ToString());
    }

    [Fact]
    public async Task CreateManualLinkAjax_WhenPlayerHasDifferentActiveLink_ReturnsConflict()
    {
        // Arrange
        var actorProfileId = Guid.NewGuid();
        var selectedUserProfileId = Guid.NewGuid();
        var differentLinkedUserProfileId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var selectedUserProfile = CreateUserProfileDto("Admin", "778899", "admin@example.com", selectedUserProfileId);

        var existingLink = CreateConnectedPlayerDto();
        var existingLinkJson = JObject.FromObject(existingLink);
        existingLinkJson["PlayerId"] = playerId;
        existingLinkJson["UserProfileId"] = differentLinkedUserProfileId;
        existingLinkJson["IsActive"] = true;
        var activeLink = existingLinkJson.ToObject<ConnectedPlayerDto>()!;

        var activeLinksResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(new CollectionModel<ConnectedPlayerDto>([activeLink]))
        {
            Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 0, top: 5)
        };

        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfile(selectedUserProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(selectedUserProfile)));

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(playerId, null, null, true, 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, activeLinksResponse));

        var sut = CreateSut(CreateSeniorAdminUser(actorProfileId));

        // Act
        var result = await sut.CreateManualLinkAjax(new ConnectedPlayersApiController.CreateManualLinkAjaxRequest
        {
            PlayerId = playerId,
            UserProfileId = selectedUserProfileId
        });

        // Assert
        Assert.IsType<ConflictObjectResult>(result);

        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.CreateConnectedPlayerLink(
            It.IsAny<CreateConnectedPlayerLinkDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UserProfileDto CreateUserProfileDto(string? displayName, string? forumId, string? email, Guid? userProfileId = null)
    {
        var json = JsonConvert.SerializeObject(new
        {
            UserProfileId = userProfileId ?? Guid.NewGuid(),
            DisplayName = displayName,
            XtremeIdiotsForumId = forumId,
            Email = email
        });

        return JsonConvert.DeserializeObject<UserProfileDto>(json)!;
    }

    private static ConnectedPlayerDto CreateConnectedPlayerDto()
    {
        var now = DateTime.UtcNow;

        var json = JsonConvert.SerializeObject(new
        {
            ConnectedPlayerProfileId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            UserProfileId = Guid.NewGuid(),
            GameType = "CallOfDuty4",
            Username = "PlayerOne",
            LinkMethod = "ActivationCode",
            LinkedAtUtc = now,
            LinkedByUserProfileId = Guid.NewGuid(),
            UnlinkedAtUtc = (DateTime?)null,
            UnlinkedByUserProfileId = (Guid?)null,
            IsActive = true
        });

        return JsonConvert.DeserializeObject<ConnectedPlayerDto>(json)!;
    }
}
