using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class MapRotationsControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<ISyncApiClient> mockSyncApiClient = new();
    private readonly IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<MapRotationsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private MapRotationsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new MapRotationsController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            mockSyncApiClient.Object,
            memoryCache,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(UserProfileClaimType.UserProfileId, Guid.NewGuid().ToString())
            ], "TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task ActivateAssignment_WhenActivationSucceedsAndRotationIsDraft_AddsPublishedPromotionAlert()
    {
        // Arrange
        var assignmentId = Guid.NewGuid();
        var mapRotationId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();
        var userProfileId = Guid.NewGuid();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(UserProfileClaimType.UserProfileId, userProfileId.ToString())
        ], "TestAuth"));

        var initialRotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty4,
            "Rotation A",
            "desc",
            "tdm",
            version: 1,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            mapRotationMaps: [],
            serverAssignments: [
                new MapRotationServerAssignmentDto(
                    assignmentId,
                    mapRotationId,
                    gameServerId,
                    DeploymentState.Synced,
                    ActivationState.Inactive,
                    deployedVersion: 1,
                    activatedVersion: null,
                    configFilePath: "maps/mp.cfg",
                    configVariableName: "sv_mapRotationCurrent",
                    lastError: null,
                    lastErrorAt: null,
                    createdAt: DateTime.UtcNow.AddDays(-1),
                    updatedAt: DateTime.UtcNow.AddHours(-1),
                    unassignedAt: null)
            ])
        {
            Status = MapRotationStatus.Draft
        };

        var freshRotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty4,
            "Rotation A",
            "desc",
            "tdm",
            version: 1,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            mapRotationMaps: [],
            serverAssignments: [])
        {
            Status = MapRotationStatus.Draft
        };

        mockRepositoryApiClient
            .SetupSequence(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(initialRotation)))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(freshRotation)));

        mockSyncApiClient
            .Setup(x => x.TriggerActivate(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true, $"maprot-activate-{assignmentId}"));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.UpdateMapRotation(
                It.Is<UpdateMapRotationDto>(dto => dto.MapRotationId == mapRotationId && dto.Status == MapRotationStatus.Active),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(freshRotation)));

        var sut = CreateSut(user);

        // Act
        var result = await sut.ActivateAssignment(assignmentId, mapRotationId);

        // Assert
        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MapRotationsController.AssignmentStatus), redirectResult.ActionName);
        Assert.Equal(assignmentId, redirectResult.RouteValues?["id"]);

        var alertsJson = sut.TempData["Alerts"]?.ToString() ?? string.Empty;
        Assert.Contains("Rotation status automatically promoted to Published.", alertsJson, StringComparison.Ordinal);
    }
}
