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
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class ProfileControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<ProfileController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private ProfileController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new ProfileController(
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
    public async Task Manage_WhenUserClaimMissing_ReturnsEmptyProfileModel()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Manage();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProfileManageViewModel>(viewResult.Model);

        Assert.Null(model.UserProfileId);
        Assert.Null(model.ActiveActivationCode);
        Assert.Empty(model.LinkedPlayers);
        Assert.Equal(0, model.TotalLinkedPlayers);
        Assert.False(model.IsLinkedPlayersCapped);
    }

    [Fact]
    public async Task ActivateConnectedPlayerCode_WhenProfileNotFound_DoesNotCallActivationEndpoint()
    {
        // Arrange
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));

        var user = new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(UserProfileClaimType.XtremeIdiotsId, "123456")
            ], "TestAuth"));

        var sut = CreateSut(user);

        // Act
        var result = await sut.ActivateConnectedPlayerCode();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProfileController.Manage), redirectResult.ActionName);

        mockRepositoryApiClient
            .Verify(x => x.ConnectedPlayers.V1.ActivateConnectedPlayerActivationCode(
                It.IsAny<ActivateConnectedPlayerActivationCodeDto>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateConnectedPlayerCode_WhenProfileFound_CallsActivationEndpoint()
    {
        // Arrange
        var userProfileId = Guid.NewGuid();
        var userProfile = CreateUserProfileDto(userProfileId);

        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));

        var activationDto = CreateActivationCodeDto(userProfileId, "ABC123");

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.ActivateConnectedPlayerActivationCode(It.IsAny<ActivateConnectedPlayerActivationCodeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConnectedPlayerActivationCodeDto>(HttpStatusCode.OK, new ApiResponse<ConnectedPlayerActivationCodeDto>(activationDto)));

        var user = new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(UserProfileClaimType.XtremeIdiotsId, "123456")
            ], "TestAuth"));

        var sut = CreateSut(user);

        // Act
        var result = await sut.ActivateConnectedPlayerCode();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProfileController.Manage), redirectResult.ActionName);

        mockRepositoryApiClient
            .Verify(x => x.ConnectedPlayers.V1.ActivateConnectedPlayerActivationCode(
                It.Is<ActivateConnectedPlayerActivationCodeDto>(dto => dto.UserProfileId == userProfileId),
                It.IsAny<CancellationToken>()), Times.Once);

        Assert.Contains("Activation code generated", sut.TempData["Alerts"]?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateConnectedPlayerCode_WhenActivationFails_AddsFailureAlert()
    {
        // Arrange
        var userProfileId = Guid.NewGuid();
        var userProfile = CreateUserProfileDto(userProfileId);

        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.ActivateConnectedPlayerActivationCode(It.IsAny<ActivateConnectedPlayerActivationCodeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConnectedPlayerActivationCodeDto>(HttpStatusCode.BadRequest));

        var user = new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(UserProfileClaimType.XtremeIdiotsId, "123456")
            ], "TestAuth"));

        var sut = CreateSut(user);

        // Act
        var result = await sut.ActivateConnectedPlayerCode();

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProfileController.Manage), redirectResult.ActionName);
        Assert.Contains("Failed to generate activation code", sut.TempData["Alerts"]?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manage_WhenProfileFound_MapsActivationAndLinkedPlayersWithCappedFlag()
    {
        // Arrange
        var userProfileId = Guid.NewGuid();
        var userProfile = CreateUserProfileDto(userProfileId);

        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));

        var activationCode = CreateActivationCodeDto(userProfileId, "QW12ER");
        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetActiveConnectedPlayerActivationCode(userProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConnectedPlayerActivationCodeDto>(HttpStatusCode.OK, new ApiResponse<ConnectedPlayerActivationCodeDto>(activationCode)));

        var linkedPlayers = new CollectionModel<ConnectedPlayerDto>([
            CreateConnectedPlayerDto(userProfileId, "Alpha", true, null),
            CreateConnectedPlayerDto(userProfileId, "Bravo", false, DateTime.UtcNow.AddDays(-1))
        ]);
        var linkedPlayersResponse = new ApiResponse<CollectionModel<ConnectedPlayerDto>>(linkedPlayers)
        {
            Pagination = new ApiPagination(totalCount: 150, filteredCount: 150, skip: 0, top: 100)
        };

        mockRepositoryApiClient
            .Setup(x => x.ConnectedPlayers.V1.GetConnectedPlayersByUserProfile(userProfileId, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK, linkedPlayersResponse));

        var user = new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(UserProfileClaimType.XtremeIdiotsId, "123456")
            ], "TestAuth"));

        var sut = CreateSut(user);

        // Act
        var result = await sut.Manage();

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProfileManageViewModel>(viewResult.Model);

        Assert.Equal(userProfileId, model.UserProfileId);
        Assert.NotNull(model.ActiveActivationCode);
        Assert.Equal("QW12ER", model.ActiveActivationCode.Code);
        Assert.Equal(2, model.LinkedPlayers.Count);
        Assert.Equal(150, model.TotalLinkedPlayers);
        Assert.True(model.IsLinkedPlayersCapped);
        Assert.Contains(model.LinkedPlayers, x => x.Username == "Alpha" && x.IsActive);
        Assert.Contains(model.LinkedPlayers, x => x.Username == "Bravo" && !x.IsActive);
    }

    private static UserProfileDto CreateUserProfileDto(Guid userProfileId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            UserProfileId = userProfileId,
            XtremeIdiotsForumId = "123456",
            DisplayName = "Test User",
            UserProfileClaims = Array.Empty<object>()
        });

        return JsonConvert.DeserializeObject<UserProfileDto>(json)!;
    }

    private static ConnectedPlayerActivationCodeDto CreateActivationCodeDto(Guid userProfileId, string code)
    {
        var json = JsonConvert.SerializeObject(new
        {
            ConnectedPlayerActivationCodeId = Guid.NewGuid(),
            UserProfileId = userProfileId,
            Code = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            AttemptCount = 0,
            MaxAttempts = 3,
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow
        });

        return JsonConvert.DeserializeObject<ConnectedPlayerActivationCodeDto>(json)!;
    }

    private static ConnectedPlayerDto CreateConnectedPlayerDto(Guid userProfileId, string username, bool isActive, DateTime? unlinkedAtUtc)
    {
        var json = JsonConvert.SerializeObject(new
        {
            ConnectedPlayerProfileId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            UserProfileId = userProfileId,
            GameType = GameType.CallOfDuty4,
            Username = username,
            LinkMethod = ConnectedPlayerLinkMethod.ActivationCode,
            LinkedAtUtc = DateTime.UtcNow.AddDays(-2),
            LinkedByUserProfileId = userProfileId,
            UnlinkedAtUtc = unlinkedAtUtc,
            UnlinkedByUserProfileId = unlinkedAtUtc.HasValue ? (Guid?)userProfileId : null,
            IsActive = isActive
        });

        return JsonConvert.DeserializeObject<ConnectedPlayerDto>(json)!;
    }
}
