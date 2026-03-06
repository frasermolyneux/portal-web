using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class DataControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<DataController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();

    private DataController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new DataController(
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
            new DataController(
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
            new DataController(
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
            new DataController(
                mockRepositoryApiClient.Object,
                telemetryClient,
                mockLogger.Object,
                null!));
    }

    [Fact]
    public async Task GetPlayersAjax_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange - empty body causes deserialization to return null model,
        // which triggers BadRequest from the null model guard
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream();

        // Act
        var result = await sut.GetPlayersAjax(null, null);

        // Assert
        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task GetMapListAjax_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream();

        // Act
        var result = await sut.GetMapListAjax(null);

        // Assert
        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task GetUsersAjax_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();
        sut.HttpContext.Request.Body = new MemoryStream();

        // Act
        var result = await sut.GetUsersAjax();

        // Assert
        Assert.IsType<BadRequestResult>(result);
    }
}
