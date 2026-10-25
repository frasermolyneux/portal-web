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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class MapRotationsControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<ISyncApiClient> mockSyncApiClient = new();
    private readonly Mock<IMapMetadataService> mockMapMetadataService = new();
    private readonly IMemoryCache memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<MapRotationsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    public MapRotationsControllerTests()
    {
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(new CollectionModel<MapRotationAssignmentOperationDto>([]))));
    }

    private MapRotationsController CreateSut(ClaimsPrincipal? user = null)
    {
        mockMapMetadataService.Setup(x => x.GetAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, MapDto>());
        var controller = new MapRotationsController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            mockSyncApiClient.Object,
            memoryCache,
            mockMapMetadataService.Object,
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
    public async Task Create_Get_WhenAuthorized_UsesDraftAsDefaultStatus()
    {
        // Arrange
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        // Act
        var result = await sut.Create();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreateMapRotationViewModel>(view.Model);
        Assert.Equal(MapRotationStatus.Draft, model.Status);
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
                It.Is<UpdateMapRotationDto>(dto => dto.MapRotationId == mapRotationId && dto.Status == MapRotationStatus.Published),
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

    [Fact]
    public async Task CreateAssignment_WhenExactDuplicateExists_ReturnsViewWithValidationError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var rotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty5,
            "Rotation 2",
            "desc",
            "dm",
            version: 2,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-2),
            updatedAt: DateTime.UtcNow.AddHours(-2),
            mapRotationMaps: [],
            serverAssignments:
            [
                new MapRotationServerAssignmentDto(
                    Guid.NewGuid(),
                    mapRotationId,
                    gameServerId,
                    DeploymentState.Synced,
                    ActivationState.Active,
                    deployedVersion: 2,
                    activatedVersion: 2,
                    configFilePath: "server.cfg",
                    configVariableName: "sv_maprotation",
                    lastError: null,
                    lastErrorAt: null,
                    createdAt: DateTime.UtcNow.AddDays(-2),
                    updatedAt: DateTime.UtcNow.AddHours(-2),
                    unassignedAt: null)
            ]);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServer(gameServerId, GameType.CallOfDuty5))));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(),
                null,
                null,
                0,
                100,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

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
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = gameServerId,
            ConfigFilePath = "server.cfg",
            ConfigVariableName = "sv_maprotation"
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<CreateMapRotationAssignmentViewModel>(viewResult.Model);
        Assert.Equal(mapRotationId, returnedModel.MapRotationId);

        Assert.True(sut.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            sut.ModelState[string.Empty]!.Errors,
            e => e.ErrorMessage.Contains("An assignment already exists for this rotation and server with the same config file path and variable name.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAssignment_WhenApiThrowsHttpRequestException_ReturnsViewWithValidationError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var rotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty5,
            "Rotation 2",
            "desc",
            "dm",
            version: 2,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-2),
            updatedAt: DateTime.UtcNow.AddHours(-2),
            mapRotationMaps: [],
            serverAssignments: []);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServer(gameServerId, GameType.CallOfDuty5))));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(),
                null,
                null,
                0,
                100,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest));

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
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = gameServerId,
            ConfigFilePath = "server.cfg",
            ConfigVariableName = "sv_maprotation"
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<CreateMapRotationAssignmentViewModel>(viewResult.Model);
        Assert.Equal(mapRotationId, returnedModel.MapRotationId);

        Assert.True(sut.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            sut.ModelState[string.Empty]!.Errors,
            e => e.ErrorMessage.Contains("Unable to create server assignment. Please review the values and try again.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditAssignment_WhenApiValidationFails_ReturnsViewWithValidationError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var assignment = new MapRotationServerAssignmentDto(
            assignmentId,
            mapRotationId,
            gameServerId,
            DeploymentState.Synced,
            ActivationState.Inactive,
            deployedVersion: 1,
            activatedVersion: null,
            configFilePath: "server.cfg",
            configVariableName: "sv_maprotation",
            lastError: null,
            lastErrorAt: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            unassignedAt: null);

        var rotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty5,
            "Rotation 2",
            "desc",
            "dm",
            version: 2,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-2),
            updatedAt: DateTime.UtcNow.AddHours(-2),
            mapRotationMaps: [],
            serverAssignments: [assignment]);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetServerAssignment(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignment)));

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.UpdateServerAssignment(It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.BadRequest, new ApiResponse(new ApiError("DUPLICATE_CONFIG_TARGET", "An assignment already exists for this rotation and server with the same config file path and variable name."))));

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
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var model = new EditMapRotationAssignmentViewModel
        {
            MapRotationServerAssignmentId = assignmentId,
            MapRotationId = mapRotationId,
            GameServerId = gameServerId,
            ConfigFilePath = "server.cfg",
            ConfigVariableName = "sv_maprotation",
            PlayerCountMin = 0,
            PlayerCountMax = 20
        };

        // Act
        var result = await sut.EditAssignment(model);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<EditMapRotationAssignmentViewModel>(viewResult.Model);
        Assert.Equal(mapRotationId, returnedModel.MapRotationId);

        Assert.True(sut.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            sut.ModelState[string.Empty]!.Errors,
            e => e.ErrorMessage.Contains("An assignment already exists for this rotation and server with the same config file path and variable name.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditAssignment_WhenApiThrowsHttpRequestException_ReturnsViewWithValidationError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var assignment = new MapRotationServerAssignmentDto(
            assignmentId,
            mapRotationId,
            gameServerId,
            DeploymentState.Synced,
            ActivationState.Inactive,
            deployedVersion: 1,
            activatedVersion: null,
            configFilePath: "server.cfg",
            configVariableName: "sv_maprotation",
            lastError: null,
            lastErrorAt: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            unassignedAt: null);

        var rotation = new MapRotationDto(
            mapRotationId,
            GameType.CallOfDuty5,
            "Rotation 2",
            "desc",
            "dm",
            version: 2,
            contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-2),
            updatedAt: DateTime.UtcNow.AddHours(-2),
            mapRotationMaps: [],
            serverAssignments: [assignment]);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetServerAssignment(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignment)));

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.UpdateServerAssignment(It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest));

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
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        var model = new EditMapRotationAssignmentViewModel
        {
            MapRotationServerAssignmentId = assignmentId,
            MapRotationId = mapRotationId,
            GameServerId = gameServerId,
            ConfigFilePath = "server.cfg",
            ConfigVariableName = "sv_maprotation",
            PlayerCountMin = 0,
            PlayerCountMax = 20
        };

        // Act
        var result = await sut.EditAssignment(model);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<EditMapRotationAssignmentViewModel>(viewResult.Model);
        Assert.Equal(mapRotationId, returnedModel.MapRotationId);

        Assert.True(sut.ModelState.ContainsKey(string.Empty));
        Assert.Contains(
            sut.ModelState[string.Empty]!.Errors,
            e => e.ErrorMessage.Contains("Unable to update server assignment. Please review the values and try again.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAssignment_Get_WhenNoAuthorizedServers_ReturnsForbid()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        var server = CreateGameServer(gameServerId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))));

        // Deny deploy for all servers
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        // Act
        var result = await sut.CreateAssignment(mapRotationId);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateAssignment_Get_WhenServerLookupFails_SurfacesErrorInsteadOfForbid()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // Repository API failure loading servers must not be misclassified as "no authorized servers"
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAssignment(mapRotationId));
    }

    [Fact]
    public async Task CreateAssignment_Get_FiltersToAuthorizedServersOnly()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var allowedServerId = Guid.NewGuid();
        var deniedServerId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        var allowedServer = CreateGameServer(allowedServerId, GameType.CallOfDuty4, "Allowed Server");
        var deniedServer = CreateGameServer(deniedServerId, GameType.CallOfDuty4, "Denied Server");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([allowedServer, deniedServer]))));

        // Allow deploy only for the allowed server tuple
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, allowedServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, deniedServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        // Act
        var result = await sut.CreateAssignment(mapRotationId);

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreateMapRotationAssignmentViewModel>(view.Model);
        Assert.Single(model.AvailableServers);
        Assert.Equal(allowedServerId, model.AvailableServers[0].GameServerId);
    }

    [Fact]
    public async Task CreateAssignment_Post_ForgedServer_ReturnsUnauthorized()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var allowedServerId = Guid.NewGuid();
        var forgedServerId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        var forgedServer = CreateGameServer(forgedServerId, GameType.CallOfDuty4, "Forged Server");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(forgedServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(forgedServer)));

        // Deny deploy for forged server
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, forgedServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = forgedServerId
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);

        // Verify no mutation occurred
        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAssignment_Post_MissingServer_ReturnsNotFound()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var missingServerId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(missingServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = missingServerId
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        Assert.IsType<NotFoundResult>(result);
        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAssignment_Post_WhenServerLookupFails_SurfacesErrorAndDoesNotCreate()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // A non-404 failure (e.g. 500) must not be misclassified as NotFound
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = serverId
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAssignment(model));

        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAssignment_Post_WhenModelStateInvalid_RedisplaysFormWithoutServerLookup()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // Servers for repopulation on validation failure
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();
        // Simulate a model-binding error (e.g. empty <select> for GameServerId)
        sut.ModelState.AddModelError(nameof(CreateMapRotationAssignmentViewModel.GameServerId), "The Game Server field is required.");

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = Guid.Empty
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert — form is redisplayed and no repository server lookup / mutation occurred
        Assert.IsType<ViewResult>(result);
        mockRepositoryApiClient.Verify(
            x => x.GameServers.V1.GetGameServer(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAssignment_Post_CrossGameSubmission_ReturnsValidationError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // Server is COD5 but rotation is COD4
        var server = CreateGameServer(serverId, GameType.CallOfDuty5, "COD5 Server");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));

        // Set up servers response for repopulation
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

        // Need deploy auth setup for repopulation (no servers will match)
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = serverId
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        Assert.True(sut.ModelState.ContainsKey(nameof(CreateMapRotationAssignmentViewModel.GameServerId)));
    }

    [Fact]
    public async Task SyncAssignment_AuthorizesWithServerTuple()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var assignment = CreateAssignment(assignmentId, mapRotationId, gameServerId);
        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4, serverAssignments: [assignment]);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // Only allow deploy for the exact server tuple
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, gameServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        mockSyncApiClient
            .Setup(x => x.TriggerSync(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true, $"maprot-sync-{assignmentId}"));

        var sut = CreateSut();

        // Act
        var result = await sut.SyncAssignment(assignmentId, mapRotationId);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MapRotationsController.AssignmentStatus), redirect.ActionName);
    }

    [Fact]
    public async Task DeleteAssignment_AuthorizesWithServerTuple_DeniedForOtherServer()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var gameServerId = Guid.NewGuid();

        var assignment = CreateAssignment(assignmentId, mapRotationId, gameServerId);
        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4, serverAssignments: [assignment]);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // Deny deploy for this server
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, gameServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        // Act
        var result = await sut.DeleteAssignment(assignmentId, mapRotationId);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);

        // Verify no mutation occurred
        mockSyncApiClient.Verify(
            x => x.TriggerRemove(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Details_CanAssignServer_ShortCircuitsOnFirstAuthorizedServer()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var allowedServerId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // A full first page (100 servers) — without short-circuit this would trigger a second page fetch.
        var firstPage = new List<GameServerDto> { CreateGameServer(allowedServerId, GameType.CallOfDuty4) };
        firstPage.AddRange(Enumerable.Range(0, 99).Select(_ => CreateGameServer(Guid.NewGuid(), GameType.CallOfDuty4)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(firstPage))));

        // Read authorization for the Details page
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        // The first server in the page is authorized for deploy
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, allowedServerId)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        // Act
        var result = await sut.Details(mapRotationId);

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MapRotationDetailsViewModel>(view.Model);
        Assert.True(model.CanAssignServer);

        // Short-circuit: only the first server was authorization-checked, and no second page was fetched.
        mockAuthorizationService.Verify(
            x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AuthPolicies.MapRotations_Deploy),
            Times.Once);
        mockRepositoryApiClient.Verify(
            x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, It.IsAny<int>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Details_WhenServerLookupFails_SurfacesErrorInsteadOfHidingUi()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        // Repository API failure loading servers must not be misclassified as "cannot assign"
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.InternalServerError));

        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Details(mapRotationId));
    }

    [Fact]
    public async Task Details_WhenServerResponseSuccessfulButMalformed_SurfacesError()
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        // A 2xx response with no Data/Items payload is malformed and must not be treated as "no servers"
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK));

        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Details(mapRotationId));
    }

    [Theory]
    [InlineData(GameType.CallOfDuty4, GameType.CallOfDuty4x)]
    [InlineData(GameType.CallOfDuty4x, GameType.CallOfDuty4)]
    public async Task CreateAssignment_Post_EquivalentGameTypes_AcceptsAssignment(GameType rotationGameType, GameType serverGameType)
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, rotationGameType);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        var server = CreateGameServer(serverId, serverGameType, "Cross-type Server");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));

        // Authorize deploy for the exact server tuple (uses rotation.GameType as the auth game type)
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, serverId, rotationGameType)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        var assignmentDto = new MapRotationServerAssignmentDto(
            Guid.NewGuid(), mapRotationId, serverId,
            DeploymentState.Pending, ActivationState.Inactive,
            deployedVersion: null, activatedVersion: null,
            configFilePath: "server.cfg", configVariableName: "sv_maprotation",
            lastError: null, lastErrorAt: null,
            createdAt: DateTime.UtcNow, updatedAt: DateTime.UtcNow,
            unassignedAt: null);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignmentDto)));

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = serverId
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert — assignment created successfully, redirects to Details
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MapRotationsController.Details), redirect.ActionName);

        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAssignment_Post_NonEquivalentGameTypes_ReturnsValidationError()
    {
        // Arrange — COD4 rotation with COD5 server should still be rejected
        var mapRotationId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, GameType.CallOfDuty4);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        var server = CreateGameServer(serverId, GameType.CallOfDuty5, "COD5 Server");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var sut = CreateSut();

        var model = new CreateMapRotationAssignmentViewModel
        {
            MapRotationId = mapRotationId,
            GameServerId = serverId
        };

        // Act
        var result = await sut.CreateAssignment(model);

        // Assert
        Assert.IsType<ViewResult>(result);
        Assert.True(sut.ModelState.ContainsKey(nameof(CreateMapRotationAssignmentViewModel.GameServerId)));

        mockRepositoryApiClient.Verify(
            x => x.MapRotations.V1.CreateServerAssignment(It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(GameType.CallOfDuty4)]
    [InlineData(GameType.CallOfDuty4x)]
    public async Task Details_CompatibleServers_QueriesBothCod4AndCod4x(GameType rotationGameType)
    {
        // Arrange
        var mapRotationId = Guid.NewGuid();
        var serverId = Guid.NewGuid();

        var rotation = CreateRotation(mapRotationId, rotationGameType);

        mockRepositoryApiClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(mapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(
                    [CreateGameServer(serverId, rotationGameType)]))));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, serverId, rotationGameType)),
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());

        var sut = CreateSut();

        // Act
        await sut.Details(mapRotationId);

        // Assert — verify GetGameServers was called with both COD4 and COD4x
        mockRepositoryApiClient.Verify(
            x => x.GameServers.V1.GetGameServers(
                It.Is<GameType[]>(gt => gt.Contains(GameType.CallOfDuty4) && gt.Contains(GameType.CallOfDuty4x)),
                null, null, 0, 100, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(DeploymentState.Pending, ActivationState.Inactive)]
    [InlineData(DeploymentState.Synced, ActivationState.Active)]
    [InlineData(DeploymentState.Failed, ActivationState.Failed)]
    [InlineData(DeploymentState.PartiallyDeployed, ActivationState.Active)]
    public async Task ApplyAssignment_RecoveryStates_QueueServerScopedApplyWithoutOtherActions(
        DeploymentState deployment, ActivationState activation)
    {
        var (sut, assignment) = SetupRecovery(deployment, activation);
        mockSyncApiClient.Setup(x => x.TriggerApply(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));

        var result = await sut.ApplyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal($"maprot-apply-{assignment.MapRotationServerAssignmentId}", sut.TempData["PendingInstanceId"]);
        Assert.Contains("queued", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        mockSyncApiClient.Verify(x => x.TriggerActivate(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        mockSyncApiClient.Verify(x => x.TriggerDeactivate(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAssignment_FailureOrWrongRelationship_DoesNotReportQueuedSuccess()
    {
        var (sut, assignment) = SetupRecovery();
        mockSyncApiClient.Setup(x => x.TriggerApply(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic failure"));

        Assert.IsType<BadRequestObjectResult>(await sut.ApplyAssignment(Guid.NewGuid(), assignment.MapRotationId));
        Assert.IsType<RedirectToActionResult>(await sut.ApplyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.Null(sut.TempData["PendingInstanceId"]);
        Assert.Contains("synthetic failure", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeploymentState.Syncing, ActivationState.Active)]
    [InlineData(DeploymentState.Removing, ActivationState.Inactive)]
    [InlineData(DeploymentState.Removed, ActivationState.Inactive)]
    [InlineData(DeploymentState.Synced, ActivationState.Activating)]
    public async Task RecoveryActions_BusyOrRemoved_DoNotQueue(DeploymentState deployment, ActivationState activation)
    {
        var (sut, assignment) = SetupRecovery(deployment, activation);
        Assert.IsType<ConflictObjectResult>(await sut.ApplyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.IsType<ConflictObjectResult>(await sut.SyncAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.IsType<ConflictObjectResult>(await sut.VerifyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.IsType<ConflictObjectResult>(await sut.ActivateAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        mockSyncApiClient.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, true, "scr_aacp_maps_1", true, "apply")]
    [InlineData(false, true, "scr_aacp_maps_1", false, "apply")]
    [InlineData(true, false, "scr_aacp_maps_1", false, "apply")]
    [InlineData(true, true, "scr_aacp_maps_2", false, "apply")]
    [InlineData(true, true, "sv_maprotation", false, "apply")]
    [InlineData(true, true, "scr_aacp_maps_1", true, "verify")]
    [InlineData(true, true, "scr_aacp_maps_1", true, "sync")]
    [InlineData(true, true, "scr_aacp_maps_1", true, "activate")]
    [InlineData(true, true, "scr_aacp_maps_1", true, "deactivate")]
    public async Task LinkedApply_CancellationAndTermination_RequirePersistedOperationAndExactSameServerRotationPair(
        bool sameServer, bool sameRotation, string sourceVariable, bool permitted, string operation)
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing);
        var source = CreateAssignment(Guid.NewGuid(),
            sameRotation ? assignment.MapRotationId : Guid.NewGuid(),
            sameServer ? assignment.GameServerId : Guid.NewGuid());
        SetDtoProperty(source, nameof(source.ConfigVariableName), sourceVariable);
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetServerAssignment(source.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(source)));
        var instanceId = $"maprot-{operation}-{source.MapRotationServerAssignmentId}";
        var operationId = Guid.NewGuid();
        SetOperation(assignment, operationId, instanceId);
        mockSyncApiClient.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, CancellationRequested: true));

        var cancellation = await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId);
        var termination = await sut.TerminateOrchestration(instanceId, assignment.MapRotationServerAssignmentId);

        if (permitted)
        {
            Assert.IsType<RedirectToActionResult>(cancellation);
            Assert.IsType<ConflictObjectResult>(termination);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(cancellation);
            Assert.IsType<BadRequestObjectResult>(termination);
        }

        mockSyncApiClient.Verify(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()),
            permitted ? Times.Once() : Times.Never());
        VerifyNoCancellationMutation();

        SetOperation(assignment, operationId, $"maprot-apply-{assignment.MapRotationServerAssignmentId}");
        Assert.IsType<BadRequestObjectResult>(await sut.TerminateOrchestration(instanceId, assignment.MapRotationServerAssignmentId));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Terminated")]
    [InlineData("Completed")]
    public async Task TerminalPollingAndRepeatedCancellation_NeverFinalizeRepositoryLifecycle(string runtimeStatus)
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing, ActivationState.Activating);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, operationId, instanceId);
        mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(), AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        mockSyncApiClient.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.GetOrchestrationStatus(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult(instanceId, runtimeStatus, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, null, Success: false)));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.IsType<RedirectToActionResult>(await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId));
            Assert.IsType<ConflictObjectResult>(await sut.TerminateOrchestration(instanceId, assignment.MapRotationServerAssignmentId));
            Assert.IsType<JsonResult>(await sut.GetSyncProgress(instanceId));
        }

        Assert.Contains("Refresh to see its recorded result", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cancelled successfully", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        VerifyNoCancellationMutation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unassign_ActiveOrUncertainConfiguration_IsBlockedEvenWhenForced(bool force)
    {
        var (sut, assignment) = SetupRecovery(activation: ActivationState.Failed);
        Assert.IsType<BadRequestObjectResult>(await sut.DeleteAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId, force));
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.DeleteServerAssignment(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RepairAndVerify_StaleActiveAssignment_DoNotRewriteConfiguration()
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.PartiallyDeployed, ActivationState.Active);
        mockSyncApiClient.Setup(x => x.TriggerSync(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.TriggerVerify(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));

        Assert.IsType<RedirectToActionResult>(await sut.SyncAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.IsType<RedirectToActionResult>(await sut.VerifyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        mockSyncApiClient.Verify(x => x.TriggerActivate(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        mockSyncApiClient.Verify(x => x.TriggerDeactivate(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        mockSyncApiClient.Verify(x => x.TriggerApply(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unassign_InactiveAssignment_UsesMetadataOnlyDeleteAndPreservesFiles()
    {
        var (sut, assignment) = SetupRecovery();
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.DeleteServerAssignment(
                assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));

        Assert.IsType<RedirectToActionResult>(await sut.DeleteAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        Assert.Contains("Server map files were preserved", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        mockSyncApiClient.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("sv_maprotation", "scr_aacp_maps_1")]
    [InlineData("SV_MAPROTATION", "SCR_AACP_MAPS_1")]
    [InlineData("Sv_MapRotation", "Scr_Aacp_Maps_1")]
    public void AssignmentGroups_OnlyCombineExactCurrentStandardPair(string mainVariable, string pickerVariable)
    {
        var main = CreateAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        SetDtoProperty(main, nameof(main.ConfigVariableName), mainVariable);
        var picker = CreateAssignment(Guid.NewGuid(), main.MapRotationId, main.GameServerId);
        SetDtoProperty(picker, nameof(picker.ConfigVariableName), pickerVariable);
        var custom = CreateAssignment(Guid.NewGuid(), main.MapRotationId, main.GameServerId);
        SetDtoProperty(custom, nameof(custom.ConfigVariableName), "scr_aacp_maps_dm3");
        var otherRotation = CreateAssignment(Guid.NewGuid(), Guid.NewGuid(), main.GameServerId);
        SetDtoProperty(otherRotation, nameof(otherRotation.ConfigVariableName), "scr_aacp_maps_1");
        var removed = CreateAssignment(Guid.NewGuid(), main.MapRotationId, main.GameServerId, DeploymentState.Removed);
        SetDtoProperty(removed, nameof(removed.ConfigVariableName), "scr_aacp_maps_1");
        var groups = new[] { picker, custom, main, otherRotation, removed }.AssignmentGroups();

        Assert.Equal(4, groups.Count);
        var combined = Assert.Single(groups, g => g.IsCombined);
        Assert.Same(main, combined.Primary);
        Assert.Equal(new[] { main, picker }, combined.Targets);
        Assert.Contains(groups, g => g.Primary == removed && !g.IsCombined);
    }

    [Fact]
    public async Task CombinedStatus_ChildRedirectsToMain_CombinesHistoriesAndFailureTruth()
    {
        var (sut, main, picker) = SetupCombined();
        SetOperation(main, Guid.NewGuid(), $"maprot-apply-{main.MapRotationServerAssignmentId}", AssignmentOperationStatus.Succeeded);
        SetOperation(picker, Guid.NewGuid(), $"maprot-apply-{main.MapRotationServerAssignmentId}", AssignmentOperationStatus.Failed);
        var redirect = Assert.IsType<RedirectToActionResult>(await sut.AssignmentStatus(picker.MapRotationServerAssignmentId));
        Assert.Equal(main.MapRotationServerAssignmentId, redirect.RouteValues!["id"]);
        var view = Assert.IsType<ViewResult>(await sut.AssignmentStatus(main.MapRotationServerAssignmentId));
        var model = Assert.IsType<AssignmentStatusViewModel>(view.Model);
        Assert.True(model.Group.IsCombined);
        Assert.Equal(2, model.Operations.Count);
        Assert.True(model.ActivityNeedsAttention);
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.GetAssignmentOperations(
            picker.MapRotationServerAssignmentId, 0, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AmbiguousStandardTargets_BlockActionsWithoutGuessing()
    {
        var (sut, main, picker) = SetupCombined();
        var duplicate = CreateAssignment(Guid.NewGuid(), main.MapRotationId, main.GameServerId);
        SetDtoProperty(duplicate, nameof(duplicate.ConfigVariableName), "SCR_AACP_MAPS_1");
        var rotation = CreateRotation(main.MapRotationId, GameType.CallOfDuty4, serverAssignments: [main, picker, duplicate]);
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetMapRotation(main.MapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));
        Assert.All(rotation.ServerAssignments.AssignmentGroups(), g => Assert.True(g.IsAmbiguous));
        Assert.IsType<ConflictObjectResult>(await sut.ApplyAssignment(main.MapRotationServerAssignmentId, main.MapRotationId));
        Assert.IsType<ConflictObjectResult>(await sut.DeleteAssignment(main.MapRotationServerAssignmentId, main.MapRotationId));
        mockSyncApiClient.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("sync")]
    [InlineData("verify")]
    [InlineData("activate")]
    [InlineData("deactivate")]
    [InlineData("remove")]
    public async Task CombinedActions_FromChild_QueueExactlyOnePrimaryRequest(string action)
    {
        var (sut, main, picker) = SetupCombined();
        mockSyncApiClient.Setup(x => x.TriggerApply(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.TriggerSync(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.TriggerVerify(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.TriggerActivate(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        mockSyncApiClient.Setup(x => x.TriggerDeactivate(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.DeleteServerAssignment(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new ApiResult(HttpStatusCode.OK));
        var result = action switch
        {
            "apply" => await sut.ApplyAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId),
            "sync" => await sut.SyncAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId),
            "verify" => await sut.VerifyAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId),
            "activate" => await sut.ActivateAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId),
            "deactivate" => await sut.DeactivateAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId),
            _ => await sut.DeleteAssignment(picker.MapRotationServerAssignmentId, main.MapRotationId)
        };
        Assert.Equal(main.MapRotationServerAssignmentId, Assert.IsType<RedirectToActionResult>(result).RouteValues!["id"]);
        if (action == "remove")
        {
            mockSyncApiClient.VerifyNoOtherCalls();
            mockRepositoryApiClient.Verify(x => x.MapRotations.V1.DeleteServerAssignment(main.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            var invocation = Assert.Single(mockSyncApiClient.Invocations);
            Assert.Equal(main.MapRotationServerAssignmentId, invocation.Arguments[0]);
        }

        VerifyNoCancellationMutation();
    }

    [Fact]
    public async Task CombinedChildBusyOrActive_BlocksNewActionsAndUnassign()
    {
        var (sut, main, picker) = SetupCombined();
        SetOperation(picker, Guid.NewGuid(), $"maprot-verify-{picker.MapRotationServerAssignmentId}");
        Assert.IsType<ConflictObjectResult>(await sut.ApplyAssignment(main.MapRotationServerAssignmentId, main.MapRotationId));
        Assert.IsType<ConflictObjectResult>(await sut.DeleteAssignment(main.MapRotationServerAssignmentId, main.MapRotationId));
        SetDtoProperty(picker, nameof(picker.ActivationState), ActivationState.Active);
        Assert.IsType<BadRequestObjectResult>(await sut.DeleteAssignment(main.MapRotationServerAssignmentId, main.MapRotationId));
        mockSyncApiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CombinedEdit_ActiveUncheckIsValidationError_NotConfigurationDeletion()
    {
        var (sut, main, picker) = SetupCombined();
        SetDtoProperty(picker, nameof(picker.ActivationState), ActivationState.Active);
        var result = await sut.EditAssignment(new EditMapRotationAssignmentViewModel
        {
            MapRotationServerAssignmentId = main.MapRotationServerAssignmentId,
            ConfigVariableName = "sv_maprotation",
            AacpEnabled = false
        });
        Assert.IsType<ViewResult>(result);
        Assert.Contains("Deactivate first", sut.ModelState[nameof(EditMapRotationAssignmentViewModel.AacpEnabled)]!.Errors[0].ErrorMessage, StringComparison.Ordinal);
        VerifyNoCancellationMutation();
    }

    [Fact]
    public async Task CombinedEdit_MixedCaseExistingTargets_LoadsEnabledMainOwnerAndAcceptsOptions()
    {
        var (sut, main, picker) = SetupCombined();
        SetDtoProperty(main, nameof(main.ConfigVariableName), "SV_MAPROTATION");
        SetDtoProperty(picker, nameof(picker.ConfigVariableName), "SCR_AACP_MAPS_1");
        var view = Assert.IsType<ViewResult>(await sut.EditAssignment(main.MapRotationServerAssignmentId));
        var model = Assert.IsType<EditMapRotationAssignmentViewModel>(view.Model);
        Assert.True(model.CanConfigureAacp);
        Assert.True(model.AacpEnabled);
        Assert.Equal(picker.ConfigFilePath, model.AacpConfigFilePath);
        Assert.Empty(model.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(model)));
        var createModel = new CreateMapRotationAssignmentViewModel
        {
            ConfigVariableName = "SV_MAPROTATION",
            AacpEnabled = true,
            AacpConfigFilePath = "aacp.cfg"
        };
        Assert.Empty(createModel.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(createModel)));

        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.UpdateServerAssignment(It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
        Assert.IsType<RedirectToActionResult>(await sut.EditAssignment(model));
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.Is<UpdateMapRotationServerAssignmentDto>(dto => dto.ConfigVariableName == "sv_maprotation"
                && dto.AacpEnabled == true && dto.AacpConfigFilePath == picker.ConfigFilePath), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssignmentEdit_OnlyNormalOwnerSendsAacpOptions(bool normalOwner)
    {
        var (sut, assignment) = SetupRecovery();
        if (!normalOwner)
            SetDtoProperty(assignment, nameof(assignment.ConfigVariableName), "scr_aacp_maps_dm3");
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.UpdateServerAssignment(It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
        Assert.IsType<RedirectToActionResult>(await sut.EditAssignment(new EditMapRotationAssignmentViewModel
        {
            MapRotationServerAssignmentId = assignment.MapRotationServerAssignmentId,
            ConfigVariableName = assignment.ConfigVariableName,
            AacpEnabled = normalOwner,
            AacpConfigFilePath = normalOwner ? "aacp.cfg" : null
        }));
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.Is<UpdateMapRotationServerAssignmentDto>(dto => dto.AacpEnabled == (normalOwner ? true : null)
                && dto.AacpConfigFilePath == (normalOwner ? "aacp.cfg" : null)), It.IsAny<CancellationToken>()), Times.Once);
    }

    private (MapRotationsController Controller, MapRotationServerAssignmentDto Main, MapRotationServerAssignmentDto Picker) SetupCombined()
    {
        var (sut, main) = SetupRecovery();
        var picker = CreateAssignment(Guid.NewGuid(), main.MapRotationId, main.GameServerId);
        SetDtoProperty(picker, nameof(picker.ConfigVariableName), "scr_aacp_maps_1");
        var rotation = CreateRotation(main.MapRotationId, GameType.CallOfDuty4, serverAssignments: [picker, main]);
        SetDtoProperty(rotation, nameof(rotation.Status), MapRotationStatus.Published);
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetMapRotation(main.MapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetServerAssignment(picker.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(picker)));
        mockRepositoryApiClient.Setup(x => x.GameServers.V1.GetGameServer(main.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(CreateGameServer(main.GameServerId, GameType.CallOfDuty4))));
        mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AuthPolicies.MapRotations_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        return (sut, main, picker);
    }

    [Fact]
    public async Task CancelOperation_MismatchedOperation_IsRejectedBeforeTermination()
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, Guid.NewGuid(), instanceId);

        Assert.IsType<BadRequestObjectResult>(await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId));
        mockSyncApiClient.VerifyNoOtherCalls();
        VerifyNoCancellationMutation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOperation_FailedOrRequestedTermination_DoesNotMutateAssignmentOrOperation(bool requested)
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, operationId, instanceId);
        mockSyncApiClient.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic refusal", CancellationRequested: requested));

        Assert.IsType<RedirectToActionResult>(await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId));
        VerifyNoCancellationMutation();
        Assert.Contains(requested ? "Cancellation requested" : "Cancellation failed", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelOperation_TerminalAcknowledgment_OnlyRefreshesRecordedSyncOutcome()
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, operationId, instanceId);
        mockSyncApiClient.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));
        await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId);
        VerifyNoCancellationMutation();
        Assert.Contains("Refresh to see its recorded result", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DelayedCancellation_AfterAnotherRequestAndNewRepair_NeverOverwritesNewBusyState()
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing, ActivationState.Activating);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, operationId, instanceId);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<SyncTriggerResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        mockSyncApiClient.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref requestCount) != 1)
                    return new SyncTriggerResult(true);
                requestStarted.SetResult();
                return await releaseResponse.Task.ConfigureAwait(false);
            });

        var delayedRequest = sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondRequest = CreateSut();
        Assert.IsType<RedirectToActionResult>(await secondRequest.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId));
        SetOperation(assignment, operationId, instanceId, AssignmentOperationStatus.Cancelled);
        SetDtoProperty(assignment, nameof(assignment.DeploymentState), DeploymentState.Failed);
        SetDtoProperty(assignment, nameof(assignment.ActivationState), ActivationState.Failed);
        mockSyncApiClient.Setup(x => x.TriggerSync(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true));
        Assert.IsType<RedirectToActionResult>(await secondRequest.SyncAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId));
        SetDtoProperty(assignment, nameof(assignment.DeploymentState), DeploymentState.Syncing);
        SetOperation(assignment, Guid.NewGuid(), $"maprot-sync-{assignment.MapRotationServerAssignmentId}");
        releaseResponse.SetResult(new SyncTriggerResult(true));
        Assert.IsType<RedirectToActionResult>(await delayedRequest);

        Assert.Equal(DeploymentState.Syncing, assignment.DeploymentState);
        mockSyncApiClient.Verify(x => x.TriggerSync(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoCancellationMutation();
    }

    [Theory]
    [InlineData(AssignmentOperationStatus.Cancelled)]
    [InlineData(AssignmentOperationStatus.Failed)]
    [InlineData(AssignmentOperationStatus.Succeeded)]
    public async Task TerminalOperation_RepeatedCancellationAndTermination_AreRejectedWithoutLifecycleWrites(AssignmentOperationStatus operationStatus)
    {
        var (sut, assignment) = SetupRecovery(DeploymentState.Syncing);
        var operationId = Guid.NewGuid();
        var instanceId = $"maprot-apply-{assignment.MapRotationServerAssignmentId}";
        SetOperation(assignment, operationId, instanceId, operationStatus);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.IsType<ConflictObjectResult>(await sut.CancelOperation(operationId, assignment.MapRotationServerAssignmentId, instanceId));
            Assert.IsType<ConflictObjectResult>(await sut.TerminateOrchestration(instanceId, assignment.MapRotationServerAssignmentId));
        }

        mockSyncApiClient.VerifyNoOtherCalls();
        VerifyNoCancellationMutation();
    }

    [Fact]
    public async Task FailedDispatches_DoNotPersistOptimisticBusyLifecycleStates()
    {
        var (sut, assignment) = SetupRecovery(activation: ActivationState.Active);
        mockSyncApiClient.Setup(x => x.TriggerApply(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic dispatch failure"));
        mockSyncApiClient.Setup(x => x.TriggerSync(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic dispatch failure"));
        mockSyncApiClient.Setup(x => x.TriggerVerify(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic dispatch failure"));
        mockSyncApiClient.Setup(x => x.TriggerActivate(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic dispatch failure"));
        mockSyncApiClient.Setup(x => x.TriggerDeactivate(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, Error: "synthetic dispatch failure"));

        await sut.ApplyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);
        await sut.SyncAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);
        await sut.VerifyAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);
        await sut.ActivateAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);
        await sut.DeactivateAssignment(assignment.MapRotationServerAssignmentId, assignment.MapRotationId);

        Assert.Null(sut.TempData["PendingInstanceId"]);
        VerifyNoCancellationMutation();
    }

    private void VerifyNoCancellationMutation()
    {
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.UpdateAssignmentOperation(It.IsAny<Guid>(),
            It.IsAny<AssignmentOperationStatus>(), It.IsAny<string>()), Times.Never);
        mockRepositoryApiClient.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private (MapRotationsController Controller, MapRotationServerAssignmentDto Assignment) SetupRecovery(
        DeploymentState deployment = DeploymentState.Synced, ActivationState activation = ActivationState.Inactive)
    {
        var assignment = CreateAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), deployment);
        SetDtoProperty(assignment, nameof(assignment.ActivationState), activation);
        var rotation = CreateRotation(assignment.MapRotationId, GameType.CallOfDuty4, serverAssignments: [assignment]);
        SetDtoProperty(rotation, nameof(rotation.Version), 2);
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetMapRotation(rotation.MapRotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetServerAssignment(assignment.MapRotationServerAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignment)));
        mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(),
                It.Is<object?>(r => IsServerTuple(r, assignment.GameServerId)), AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());
        return (CreateSut(), assignment);
    }

    private void SetOperation(MapRotationServerAssignmentDto assignment, Guid operationId, string instanceId,
        AssignmentOperationStatus status = AssignmentOperationStatus.InProgress)
    {
        var operation = Newtonsoft.Json.JsonConvert.DeserializeObject<MapRotationAssignmentOperationDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                MapRotationAssignmentOperationId = operationId,
                assignment.MapRotationServerAssignmentId,
                DurableFunctionInstanceId = instanceId,
                Status = status,
                StartedAt = DateTime.UtcNow
            }))!;
        mockRepositoryApiClient.Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                assignment.MapRotationServerAssignmentId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(new CollectionModel<MapRotationAssignmentOperationDto>([operation]))));
    }

    #region Helpers

    private static bool IsServerTuple(object? resource, Guid expectedServerId)
    {
        return resource is ValueTuple<GameType, Guid> tuple && tuple.Item2 == expectedServerId;
    }

    private static bool IsServerTuple(object? resource, Guid expectedServerId, GameType expectedGameType)
    {
        return resource is ValueTuple<GameType, Guid> tuple
            && tuple.Item2 == expectedServerId
            && tuple.Item1 == expectedGameType;
    }

    private static MapRotationDto CreateRotation(
        Guid id,
        GameType gameType,
        string title = "Test Rotation",
        List<MapRotationServerAssignmentDto>? serverAssignments = null)
    {
        return new MapRotationDto(
            id, gameType, title, "desc", "tdm",
            version: 1, contentHash: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            mapRotationMaps: [],
            serverAssignments: serverAssignments ?? []);
    }

    private static MapRotationServerAssignmentDto CreateAssignment(
        Guid assignmentId,
        Guid mapRotationId,
        Guid gameServerId,
        DeploymentState deploymentState = DeploymentState.Synced)
    {
        return new MapRotationServerAssignmentDto(
            assignmentId, mapRotationId, gameServerId,
            deploymentState, ActivationState.Inactive,
            deployedVersion: 1, activatedVersion: null,
            configFilePath: "server.cfg",
            configVariableName: "sv_maprotation",
            lastError: null, lastErrorAt: null,
            createdAt: DateTime.UtcNow.AddDays(-1),
            updatedAt: DateTime.UtcNow.AddHours(-1),
            unassignedAt: null);
    }

    private static GameServerDto CreateGameServer(Guid id, GameType gameType, string title = "Test Server")
    {
        var dto = new GameServerDto();
        SetDtoProperty(dto, nameof(GameServerDto.GameServerId), id);
        SetDtoProperty(dto, nameof(GameServerDto.GameType), gameType);
        SetDtoProperty(dto, nameof(GameServerDto.Title), title);
        return dto;
    }

    private static void SetDtoProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        property?.SetValue(target, value);
    }

    #endregion
}
