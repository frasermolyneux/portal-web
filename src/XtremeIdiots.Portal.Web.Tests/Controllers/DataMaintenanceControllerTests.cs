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
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class DataMaintenanceControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<DataMaintenanceController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private DataMaintenanceController CreateSut()
    {
        var controller = new DataMaintenanceController(
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext();

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task Index_ReturnsViewWithModel()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Index();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task LookupPlayer_WithInvalidGuid_ReturnsViewAndDoesNotCallApi()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.LookupPlayer("not-a-guid");

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);
        Assert.Null(model.Player);

        mockRepositoryApiClient.Verify(x => x.Players.V1.GetPlayer(
            It.IsAny<Guid>(),
            It.IsAny<PlayerEntityOptions>()), Times.Never);
    }

    [Fact]
    public async Task LookupPlayer_WithExistingPlayer_ReturnsPreviewModel()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var playerDto = CreatePlayerDto(playerId, aliasCount: 2, ipAddressCount: 4, adminActionCount: 1, protectedNameCount: 3, relatedPlayerCount: 5, tagCount: 6);

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.OK, new ApiResponse<PlayerDto>(playerDto)));

        var sut = CreateSut();

        // Act
        var result = await sut.LookupPlayer(playerId.ToString());

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);

        Assert.NotNull(model.Player);
        Assert.Equal(playerId, model.Player.PlayerId);
        Assert.Equal(2, model.Player.AliasCount);
        Assert.Equal(4, model.Player.IpAddressCount);
        Assert.Equal(1, model.Player.AdminActionCount);
        Assert.Equal(3, model.Player.ProtectedNameCount);
        Assert.Equal(5, model.Player.RelatedPlayerCount);
        Assert.Equal(6, model.Player.TagCount);
    }

    [Fact]
    public async Task LookupPlayer_WhenApiFails_ReturnsViewWithNoPlayer()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        // Act
        var result = await sut.LookupPlayer(playerId.ToString());

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);

        Assert.Null(model.Player);
    }

    [Fact]
    public async Task LookupPlayer_WhenPlayerNotFound_ReturnsViewWithNoPlayer()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.NotFound));

        var sut = CreateSut();

        // Act
        var result = await sut.LookupPlayer(playerId.ToString());

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);

        Assert.Null(model.Player);
    }

    [Fact]
    public async Task DeletePlayer_WhenConfirmationDoesNotMatch_DoesNotCallDelete()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var playerDto = CreatePlayerDto(playerId);

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.OK, new ApiResponse<PlayerDto>(playerDto)));

        var sut = CreateSut();

        // Act
        var result = await sut.DeletePlayer(playerId, "different-value");

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);

        Assert.NotNull(model.Player);

        mockRepositoryApiClient.Verify(x => x.DataMaintenance.V1.DeletePlayer(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletePlayer_WhenConfirmationMatches_CallsDeleteAndRedirects()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.DataMaintenance.V1.DeletePlayer(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));

        var sut = CreateSut();

        // Act
        var result = await sut.DeletePlayer(playerId, playerId.ToString());

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DataMaintenanceController.Index), redirect.ActionName);

        mockRepositoryApiClient.Verify(x => x.DataMaintenance.V1.DeletePlayer(
            playerId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePlayer_WhenDeleteFails_ReturnsView()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.DataMaintenance.V1.DeletePlayer(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.InternalServerError));

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.NotFound));

        var sut = CreateSut();

        // Act
        var result = await sut.DeletePlayer(playerId, playerId.ToString());

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DataMaintenanceViewModel>(viewResult.Model);
        Assert.Equal(playerId.ToString(), model.LookupPlayerId);
    }

    [Fact]
    public async Task DeletePlayer_WhenDeleteReturnsNotFound_RedirectsToIndex()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.DataMaintenance.V1.DeletePlayer(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.NotFound));

        var sut = CreateSut();

        // Act
        var result = await sut.DeletePlayer(playerId, playerId.ToString());

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DataMaintenanceController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task DeletePlayer_WhenConfirmationMismatchesAndPreviewNotFound_AddsModelLevelError()
    {
        // Arrange
        var playerId = Guid.NewGuid();

        mockRepositoryApiClient
            .Setup(x => x.Players.V1.GetPlayer(playerId, PlayerEntityOptions.Counts))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.NotFound));

        var sut = CreateSut();

        // Act
        var result = await sut.DeletePlayer(playerId, "different-value");

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(viewResult.ViewData.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            viewResult.ViewData.ModelState[string.Empty]!.Errors,
            x => x.ErrorMessage.Contains("Player preview is unavailable", StringComparison.Ordinal));
    }

    private static PlayerDto CreatePlayerDto(
        Guid playerId,
        int aliasCount = 0,
        int ipAddressCount = 0,
        int adminActionCount = 0,
        int protectedNameCount = 0,
        int relatedPlayerCount = 0,
        int tagCount = 0)
    {
        var json = JsonConvert.SerializeObject(new
        {
            PlayerId = playerId,
            GameType = "CallOfDuty4",
            Username = "PlayerOne",
            Guid = "ABCDEF",
            IpAddress = "127.0.0.1",
            LastSeen = DateTime.UtcNow,
            AliasCount = aliasCount,
            IpAddressCount = ipAddressCount,
            AdminActionCount = adminActionCount,
            ProtectedNameCount = protectedNameCount,
            RelatedPlayerCount = relatedPlayerCount,
            TagCount = tagCount
        });

        return JsonConvert.DeserializeObject<PlayerDto>(json)!;
    }
}
