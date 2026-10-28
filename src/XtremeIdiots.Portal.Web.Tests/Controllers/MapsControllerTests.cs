using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class MapsControllerTests : IDisposable
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new() { DefaultValue = DefaultValue.Mock };
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly DependencyReadCoordinator coordinator = new();
    private readonly ServiceProvider services;

    public MapsControllerTests()
    {
        services = new ServiceCollection().AddSingleton(mockRepositoryApiClient.Object).BuildServiceProvider();
    }

    private MapExperienceService Maps()
    {
        mockRepositoryApiClient.Setup(r => r.Maps.V1.GetMapGames(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapGamesDto>(HttpStatusCode.OK,
                new ApiResponse<MapGamesDto>(new MapGamesDto { Games = [GameType.CallOfDuty4] })));
        return new(mockRepositoryApiClient.Object, cache, coordinator, services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose()
    {
        services.Dispose();
        cache.Dispose();
        coordinator.Dispose();
        GC.SuppressFinalize(this);
    }
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<MapsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private MapsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new MapsController(
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

        return controller;
    }

    [Fact]
    public async Task GameIndex_WithAbsentGame_ClearsBookmarkedFilter()
    {
        var result = Assert.IsType<ViewResult>(await CreateSut().GameIndex(GameType.CallOfDuty5, Maps()));
        Assert.Null(result.ViewData["GameType"]);
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
            new MapsController(mockRepositoryApiClient.Object, null!, mockLogger.Object, mockConfiguration.Object, auditLogger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MapsController(mockRepositoryApiClient.Object, telemetryClient, null!, mockConfiguration.Object, auditLogger));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MapsController(mockRepositoryApiClient.Object, telemetryClient, mockLogger.Object, null!, auditLogger));
    }

    [Fact]
    public async Task Index_ReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Index(Maps());

        // Assert
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GameIndex_WithNullGameType_ReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(null, Maps());

        // Assert
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task GameIndex_Cod4xUsesTheSharedCatalogue()
    {
        var sut = CreateSut();

        var result = Assert.IsType<ViewResult>(await sut.GameIndex(GameType.CallOfDuty4x, Maps()));

        Assert.Equal("Index", result.ViewName);
        Assert.Equal(GameType.CallOfDuty4, result.ViewData["GameType"]);
    }

    [Fact]
    public async Task GameIndex_WithGameType_SetsViewDataAndReturnsViewResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(GameType.CallOfDuty4, Maps());

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Equal(GameType.CallOfDuty4, sut.ViewData["GameType"]);
    }
}
