using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class MapsControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<MapsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();

    private MapsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new MapsController(
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };
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
    public void Constructor_WithNullTelemetryClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MapsController(mockRepositoryApiClient.Object, null!, mockLogger.Object, mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MapsController(mockRepositoryApiClient.Object, telemetryClient, null!, mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MapsController(mockRepositoryApiClient.Object, telemetryClient, mockLogger.Object, null!));
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
        var result = await sut.GameIndex(GameType.CallOfDuty4);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Equal(GameType.CallOfDuty4, sut.ViewData["GameType"]);
    }
}
