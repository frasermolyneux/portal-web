using System.Net;
using System.Security.Claims;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class ConnectedPlayersControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<ConnectedPlayersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private ConnectedPlayersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new ConnectedPlayersController(
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task Index_WhenApiSucceeds_ReturnsViewModel()
    {
        // Arrange
        var collection = new CollectionModel<ConnectedPlayerDto>([
            CreateConnectedPlayerDto(true)
        ]);
        var apiResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(collection)
        {
            Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 0, top: 500)
        };

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, apiResponse));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConnectedPlayersAdminViewModel>(viewResult.Model);

        Assert.Single(model.ConnectedPlayers);
        Assert.True(model.IsSeniorAdmin);
        Assert.Equal(1, model.TotalCount);
    }

    [Fact]
    public async Task Index_WhenApiFails_RedirectsToErrorPage()
    {
        // Arrange
        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.InternalServerError));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString())
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ErrorsController.Display), redirect.ActionName);
        Assert.Equal("Errors", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_WhenMultiplePages_AggregatesAllRows()
    {
        // Arrange
        var firstPageCollection = new CollectionModel<ConnectedPlayerDto>([
            CreateConnectedPlayerDto(true)
        ]);
        var firstPageResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(firstPageCollection)
        {
            Pagination = new ApiPagination(totalCount: 2, filteredCount: 2, skip: 0, top: 500)
        };

        var secondPageCollection = new CollectionModel<ConnectedPlayerDto>([
            CreateConnectedPlayerDto(false)
        ]);
        var secondPageResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(secondPageCollection)
        {
            Pagination = new ApiPagination(totalCount: 2, filteredCount: 2, skip: 1, top: 500)
        };

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, firstPageResponse));

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 1, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, secondPageResponse));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConnectedPlayersAdminViewModel>(viewResult.Model);

        Assert.Equal(2, model.ConnectedPlayers.Count);
        Assert.Equal(2, model.TotalCount);

        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()), Times.Once);
        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 1, 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_WhenSecondPageFails_RedirectsToErrorPage()
    {
        // Arrange
        var firstPageCollection = new CollectionModel<ConnectedPlayerDto>([
            CreateConnectedPlayerDto(true)
        ]);
        var firstPageResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(firstPageCollection)
        {
            Pagination = new ApiPagination(totalCount: 2, filteredCount: 2, skip: 0, top: 500)
        };

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, firstPageResponse));

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayers(null, null, null, null, 1, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.InternalServerError));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ErrorsController.Display), redirect.ActionName);
        Assert.Equal("Errors", redirect.ControllerName);
    }

    [Fact]
    public async Task CreateManualLink_WhenNotSeniorAdmin_ReturnsForbid()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.CreateManualLink(new CreateConnectedPlayerLinkInput
        {
            PlayerId = Guid.NewGuid(),
            UserProfileId = Guid.NewGuid()
        });

        // Assert
        Assert.IsType<ForbidResult>(result);

        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.CreateConnectedPlayerLink(
            It.IsAny<CreateConnectedPlayerLinkDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateManualLink_WhenSeniorAdmin_CallsCreateConnectedPlayerLink()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var userProfileId = Guid.NewGuid();
        var actorProfileId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.CreateConnectedPlayerLink(It.IsAny<CreateConnectedPlayerLinkDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.Created));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true"),
            new Claim(UserProfileClaimType.UserProfileId, actorProfileId.ToString())
        ], "TestAuth")));

        // Act
        var result = await sut.CreateManualLink(new CreateConnectedPlayerLinkInput
        {
            PlayerId = playerId,
            UserProfileId = userProfileId
        });

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConnectedPlayersController.Index), redirect.ActionName);

        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.CreateConnectedPlayerLink(
            It.Is<CreateConnectedPlayerLinkDto>(dto =>
                dto.PlayerId == playerId &&
                dto.UserProfileId == userProfileId &&
                dto.LinkedByUserProfileId == actorProfileId &&
                dto.LinkMethod == ConnectedPlayerLinkMethod.AdminForced),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateManualLink_WhenSeniorAdminMissingProfileId_ReturnsForbid()
    {
        // Arrange
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.CreateManualLink(new CreateConnectedPlayerLinkInput
        {
            PlayerId = Guid.NewGuid(),
            UserProfileId = Guid.NewGuid()
        });

        // Assert
        Assert.IsType<ForbidResult>(result);
        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.CreateConnectedPlayerLink(
            It.IsAny<CreateConnectedPlayerLinkDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForceUnlink_WhenNotSeniorAdmin_ReturnsForbid()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.ForceUnlink(Guid.NewGuid());

        // Assert
        Assert.IsType<ForbidResult>(result);
        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.ForceUnlinkConnectedPlayer(
            It.IsAny<Guid>(),
            It.IsAny<ForceUnlinkConnectedPlayerDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForceUnlink_WhenSeniorAdmin_CallsForceUnlink()
    {
        // Arrange
        var connectedPlayerProfileId = Guid.NewGuid();
        var actorProfileId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.ForceUnlinkConnectedPlayer(It.IsAny<Guid>(), It.IsAny<ForceUnlinkConnectedPlayerDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.NoContent));

        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true"),
            new Claim(UserProfileClaimType.UserProfileId, actorProfileId.ToString())
        ], "TestAuth")));

        // Act
        var result = await sut.ForceUnlink(connectedPlayerProfileId);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ConnectedPlayersController.Index), redirect.ActionName);

        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.ForceUnlinkConnectedPlayer(
            connectedPlayerProfileId,
            It.Is<ForceUnlinkConnectedPlayerDto>(dto => dto.UnlinkedByUserProfileId == actorProfileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForceUnlink_WhenSeniorAdminMissingProfileId_ReturnsForbid()
    {
        // Arrange
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.ForceUnlink(Guid.NewGuid());

        // Assert
        Assert.IsType<ForbidResult>(result);
        mockRepositoryApiClient.Verify(x => x.ConnectedPlayers.V1.ForceUnlinkConnectedPlayer(
            It.IsAny<Guid>(),
            It.IsAny<ForceUnlinkConnectedPlayerDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ConnectedPlayerDto CreateConnectedPlayerDto(bool isActive)
    {
        var now = DateTime.UtcNow;

        var json = JsonConvert.SerializeObject(new
        {
            ConnectedPlayerProfileId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            UserProfileId = Guid.NewGuid(),
            GameType = GameType.CallOfDuty4,
            Username = "TestPlayer",
            LinkMethod = ConnectedPlayerLinkMethod.ActivationCode,
            LinkedAtUtc = now,
            LinkedByUserProfileId = Guid.NewGuid(),
            UnlinkedAtUtc = isActive ? (DateTime?)null : now.AddMinutes(1),
            UnlinkedByUserProfileId = isActive ? (Guid?)null : Guid.NewGuid(),
            IsActive = isActive
        });

        return JsonConvert.DeserializeObject<ConnectedPlayerDto>(json)!;
    }
}
