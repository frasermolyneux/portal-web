using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.GeoLocation.Api.Client.V1;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class PlayersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IGeoLocationApiClient> mockGeoLocationClient = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<IProxyCheckService> mockProxyCheckService = new();
    private readonly Mock<ILogger<PlayersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();

    private PlayersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new PlayersController(
            mockAuthorizationService.Object,
            mockGeoLocationClient.Object,
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockProxyCheckService.Object,
            mockLogger.Object,
            mockConfiguration.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var sut = CreateSut();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public async Task Index_ReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Index();

        // Assert
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GameIndex_WithNullGameType_ReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(null);

        // Assert
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GameIndex_WithGameType_SetsViewDataAndReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(GameType.CallOfDuty2);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Equal(GameType.CallOfDuty2, sut.ViewData["GameType"]);
    }
}
