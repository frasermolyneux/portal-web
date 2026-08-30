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
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

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

    #region Helpers

    private static bool IsServerTuple(object? resource, Guid expectedServerId)
    {
        return resource is ValueTuple<GameType, Guid> tuple && tuple.Item2 == expectedServerId;
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
