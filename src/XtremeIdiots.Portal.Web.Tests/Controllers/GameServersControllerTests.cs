using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GameServersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<GameServersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();

    private GameServersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new GameServersController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            telemetryClient,
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
    public void Constructor_WithNullTelemetryClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                null!,
                mockLogger.Object,
                mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                telemetryClient,
                null!,
                mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                telemetryClient,
                mockLogger.Object,
                null!));
    }
}
