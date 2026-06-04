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
using System.Net;
using System.Security.Claims;
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
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.SeniorAdmin, "true")
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConnectedPlayersAdminViewModel>(viewResult.Model);

        Assert.Empty(model.ConnectedPlayers);
        Assert.True(model.IsSeniorAdmin);
        Assert.Equal(0, model.TotalCount);
    }

    [Fact]
    public async Task Index_WhenUserIsNotSeniorAdmin_ReturnsViewModelWithNoSeniorAdminFlag()
    {
        // Arrange
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString())
        ], "TestAuth")));

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ConnectedPlayersAdminViewModel>(viewResult.Model);

        Assert.Empty(model.ConnectedPlayers);
        Assert.False(model.IsSeniorAdmin);
        Assert.Equal(0, model.TotalCount);
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
}
