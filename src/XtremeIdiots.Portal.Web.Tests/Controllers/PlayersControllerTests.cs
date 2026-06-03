using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class PlayersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IGeoLocationApiClient> mockGeoLocationClient = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<PlayersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private PlayersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new PlayersController(
            mockAuthorizationService.Object,
            mockGeoLocationClient.Object,
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
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var sut = CreateSut();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public async Task Index_ReturnsViewResult_WithTagOptionsModel()
    {
        // Arrange
        SetupTagsResponse();
        var sut = CreateSut();

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlayersIndexViewModel>(viewResult.Model);

        Assert.Null(model.SelectedGameType);
        Assert.Single(model.Tags);
        Assert.Equal("Trusted", model.Tags[0].Name);

        mockRepositoryApiClient.Verify(x => x.Tags.V1.GetTags(0, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GameIndex_WithNullGameType_ReturnsViewResult()
    {
        // Arrange
        SetupTagsResponse();
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(null);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlayersIndexViewModel>(viewResult.Model);

        Assert.Equal("Index", viewResult.ViewName);
        Assert.Null(model.SelectedGameType);
    }

    [Fact]
    public async Task GameIndex_WithGameType_SetsViewDataAndReturnsViewResult()
    {
        // Arrange
        SetupTagsResponse();
        var sut = CreateSut();

        // Act
        var result = await sut.GameIndex(GameType.CallOfDuty2);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlayersIndexViewModel>(viewResult.Model);

        Assert.Equal("Index", viewResult.ViewName);
        Assert.Equal(GameType.CallOfDuty2, model.SelectedGameType);
        Assert.Single(model.Tags);
    }

    private void SetupTagsResponse()
    {
        var tags = new CollectionModel<TagDto>([new TagDto { TagId = Guid.NewGuid(), Name = "Trusted", TagHtml = "<span class=\"badge bg-success\">Trusted</span>" }]);
        var response = new ApiResponse<CollectionModel<TagDto>>(tags)
        {
            Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 0, top: 100)
        };

        mockRepositoryApiClient
            .Setup(x => x.Tags.V1.GetTags(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(HttpStatusCode.OK, response));
    }
}
