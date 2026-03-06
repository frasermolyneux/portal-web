using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.InvisionCommunity.Api.Abstractions;
using System.Security.Claims;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class HealthCheckControllerTests
{
    private readonly Mock<IInvisionApiClient> mockForumsClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<HealthCheckController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();

    public HealthCheckControllerTests()
    {
        mockConfiguration.Setup(c => c["XtremeIdiots:Forums:BaseUrl"])
            .Returns("https://www.xtremeidiots.com");
    }

    private HealthCheckController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new HealthCheckController(
            mockForumsClient.Object,
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
    public void Constructor_WithNullForumsClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HealthCheckController(
                null!,
                telemetryClient,
                mockLogger.Object,
                mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullTelemetryClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HealthCheckController(
                mockForumsClient.Object,
                null!,
                mockLogger.Object,
                mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HealthCheckController(
                mockForumsClient.Object,
                telemetryClient,
                null!,
                mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HealthCheckController(
                mockForumsClient.Object,
                telemetryClient,
                mockLogger.Object,
                null!));
    }

    [Fact]
    public void Constructor_WithNoForumsBaseUrl_UsesDefaultUrl()
    {
        // Arrange
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["XtremeIdiots:Forums:BaseUrl"]).Returns((string?)null);

        // Act - should not throw, uses default URL fallback
        var sut = new HealthCheckController(
            mockForumsClient.Object,
            telemetryClient,
            mockLogger.Object,
            config.Object);

        // Assert
        Assert.NotNull(sut);
    }
}
