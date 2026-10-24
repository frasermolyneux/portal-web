using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.MapRotations_Read)]
public class MapRotationsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    ISyncApiClient syncApiClient,
    IMemoryCache memoryCache,
    IMapMetadataService mapMetadataService,
    TelemetryClient telemetryClient,
    ILogger<MapRotationsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    private readonly static GameType[] supportedGameTypes = [GameType.CallOfDuty4, GameType.CallOfDuty4x, GameType.CallOfDuty5];
    private readonly static string[] assignmentOperationNames = ["apply", "sync", "activate", "deactivate", "remove", "verify"];
    private const string PendingInstanceKey = "PendingInstanceId";

    private async Task PopulateInitialMapsViewBag(List<Guid> mapIds, CancellationToken cancellationToken = default)
    {
        var initialMaps = new List<object>();
        var maps = await mapMetadataService.GetAsync(mapIds, cancellationToken).ConfigureAwait(false);
        foreach (var mapId in mapIds)
        {
            if (maps.TryGetValue(mapId, out var map))
            {
                initialMaps.Add(new { id = map.MapId.ToString(), text = map.MapName, imageUrl = !string.IsNullOrEmpty(map.MapImageUri) ? map.MapImageUri : "/images/noimage.jpg" });
            }
        }

        ViewBag.InitialMaps = System.Text.Json.JsonSerializer.Serialize(initialMaps);
    }

    [HttpGet]
    public async Task<IActionResult> Index(GameType? gameType, CancellationToken cancellationToken = default)
    {
        return gameType.HasValue && !supportedGameTypes.Contains(gameType.Value)
            ? BadRequest("Only Call of Duty 4, Call of Duty 4x, and Call of Duty 5 are supported.")
            : await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameTypes = gameType.HasValue ? [gameType.Value] : Array.Empty<GameType>();

            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotations(
                gameTypes, null, null, 0, 100, MapRotationsOrder.TitleAsc, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data?.Items == null)
                return StatusCode(502, "Unable to load map rotations. Please retry.");

            var rotations = apiResponse.IsSuccess && apiResponse.Result?.Data?.Items != null
                ? apiResponse.Result.Data.Items.ToList()
                : [];

            return View(new MapRotationsIndexViewModel
            {
                MapRotations = rotations,
                SelectedGameType = gameType
            });
        }, nameof(Index)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var canCreate = await authorizationService.AuthorizeAsync(User, PotentialAccessProbe.Instance, AuthPolicies.MapRotations_Write).ConfigureAwait(false);
        return !canCreate.Succeeded
            ? Forbid()
            : View(new CreateMapRotationViewModel
            {
                Title = string.Empty,
                GameMode = string.Empty
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateMapRotationViewModel model, CancellationToken cancellationToken = default)
    {
        if (!supportedGameTypes.Contains(model.GameType))
        {
            ModelState.AddModelError(nameof(model.GameType), "Only Call of Duty 4, Call of Duty 4x, and Call of Duty 5 are supported.");
            await PopulateInitialMapsViewBag(model.MapIds, cancellationToken).ConfigureAwait(false);
            return View(model);
        }

        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                model.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Create),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var modelValidationResult = CheckModelState(model);
            if (modelValidationResult != null)
                return modelValidationResult;

            var createDto = new CreateMapRotationDto(model.GameType, model.Title, model.GameMode)
            {
                Description = model.Description,
                Status = model.Status,
                Category = model.Category,
                SequenceOrder = model.SequenceOrder,
                CreatedByUserId = Guid.TryParse(User.UserProfileId(), out var upId) ? upId : null,
                MapIds = model.MapIds
            };

            var apiResponse = await repositoryApiClient.MapRotations.V1.CreateMapRotation(createDto, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess)
            {
                if (apiResponse.Result?.Errors != null)
                {
                    foreach (var error in apiResponse.Result.Errors)
                    {
                        ModelState.AddModelError(error.Target ?? string.Empty, error.Message ?? "An error occurred");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "An error occurred while creating the map rotation");
                }

                await PopulateInitialMapsViewBag(model.MapIds, cancellationToken).ConfigureAwait(false);
                return View(model);
            }

            TrackSuccessTelemetry("MapRotationCreated", nameof(Create), new Dictionary<string, string>
            {
                { nameof(model.GameType), model.GameType.ToString() },
                { nameof(model.Title), model.Title }
            });

            this.AddAlertSuccess($"Map rotation '{model.Title}' has been created successfully.");

            var createdId = apiResponse.Result?.Data?.MapRotationId;
            return createdId.HasValue && createdId.Value != Guid.Empty
                ? RedirectToAction(nameof(Details), new { id = createdId.Value })
                : RedirectToAction(nameof(Index));
        }, nameof(Create)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Clone(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (apiResponse.IsNotFound || apiResponse.Result?.Data is null)
                return NotFound();

            var rotation = apiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Clone),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            await PopulateInitialMapsViewBag(
                rotation.MapRotationMaps?.OrderBy(m => m.SortOrder).Select(m => m.MapId).ToList() ?? [],
                cancellationToken).ConfigureAwait(false);

            return View("Create", new CreateMapRotationViewModel
            {
                GameType = rotation.GameType,
                Title = $"{rotation.Title} (Copy)",
                Description = rotation.Description,
                GameMode = rotation.GameMode,
                Status = MapRotationStatus.Draft,
                Category = rotation.Category,
                MapIds = rotation.MapRotationMaps?.OrderBy(m => m.SortOrder).Select(m => m.MapId).ToList() ?? []
            });
        }, nameof(Clone)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (apiResponse.IsNotFound || apiResponse.Result?.Data is null)
                return NotFound();

            var rotation = apiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Edit),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            await PopulateInitialMapsViewBag(
                rotation.MapRotationMaps?.OrderBy(m => m.SortOrder).Select(m => m.MapId).ToList() ?? [],
                cancellationToken).ConfigureAwait(false);

            return View(new EditMapRotationViewModel
            {
                MapRotationId = rotation.MapRotationId,
                Title = rotation.Title,
                Description = rotation.Description,
                GameMode = rotation.GameMode,
                Status = rotation.Status,
                Category = rotation.Category,
                SequenceOrder = rotation.SequenceOrder,
                GameType = rotation.GameType,
                Version = rotation.Version,
                MapIds = rotation.MapRotationMaps?.OrderBy(m => m.SortOrder).Select(m => m.MapId).ToList() ?? []
            });
        }, nameof(Edit)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditMapRotationViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            // Load the rotation from the API to authorize against the server-side GameType
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(model.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Edit),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var modelValidationResult = CheckModelState(model);
            if (modelValidationResult != null)
                return modelValidationResult;

            var updateDto = new UpdateMapRotationDto(model.MapRotationId)
            {
                Title = model.Title,
                Description = model.Description,
                GameMode = model.GameMode,
                Status = model.Status,
                Category = model.Category,
                SequenceOrder = model.SequenceOrder,
                LastModifiedByUserId = Guid.TryParse(User.UserProfileId(), out var editUpId) ? editUpId : null,
                MapIds = model.MapIds
            };

            var apiResponse = await repositoryApiClient.MapRotations.V1.UpdateMapRotation(updateDto, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess)
            {
                if (apiResponse.Result?.Errors != null)
                {
                    foreach (var error in apiResponse.Result.Errors)
                    {
                        ModelState.AddModelError(error.Target ?? string.Empty, error.Message ?? "An error occurred");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "An error occurred while updating the map rotation");
                }

                await PopulateInitialMapsViewBag(model.MapIds, cancellationToken).ConfigureAwait(false);
                return View(model);
            }

            TrackSuccessTelemetry("MapRotationUpdated", nameof(Edit), new Dictionary<string, string>
            {
                { nameof(model.MapRotationId), model.MapRotationId.ToString() },
                { nameof(model.Title), model.Title }
            });

            this.AddAlertSuccess($"Map rotation '{model.Title}' has been updated successfully.");

            return RedirectToAction(nameof(Details), new { id = model.MapRotationId });
        }, nameof(Edit)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (apiResponse.IsNotFound || apiResponse.Result?.Data is null)
                return NotFound();

            var rotation = apiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Read,
                nameof(Details),
                "MapRotation",
                $"MapRotationId:{id},GameType:{rotation.GameType}").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var maps = new List<MapDto>();
            if (rotation.MapRotationMaps?.Count > 0)
            {
                var metadata = await mapMetadataService.GetAsync(
                    rotation.MapRotationMaps.Select(m => m.MapId), cancellationToken).ConfigureAwait(false);
                foreach (var rotationMap in rotation.MapRotationMaps.OrderBy(m => m.SortOrder))
                {
                    if (metadata.TryGetValue(rotationMap.MapId, out var map))
                        maps.Add(map);
                }
            }

            // Load game server details and operations for each assignment
            var assignmentOperations = new Dictionary<Guid, List<MapRotationAssignmentOperationDto>>();
            if (rotation.ServerAssignments?.Count > 0)
            {
                var gameServerIds = rotation.ServerAssignments
                    .Select(a => a.GameServerId)
                    .Distinct()
                    .ToArray();

                if (gameServerIds.Length > 0)
                {
                    const int gameServersPageSize = 100;
                    for (var offset = 0; offset < gameServerIds.Length; offset += gameServersPageSize)
                    {
                        var chunk = gameServerIds
                            .Skip(offset)
                            .Take(gameServersPageSize)
                            .ToArray();

                        var serversResponse = await repositoryApiClient.GameServers.V1
                            .GetGameServers(null, chunk, null, 0, chunk.Length, null, cancellationToken)
                            .ConfigureAwait(false);

                        if (serversResponse.IsSuccess && serversResponse.Result?.Data?.Items is not null)
                        {
                            foreach (var server in serversResponse.Result.Data.Items)
                            {
                                ViewData[$"Server_{server.GameServerId}"] = server;
                            }
                        }
                    }
                }

                foreach (var assignment in rotation.ServerAssignments)
                {
                    var opsResponse = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(assignment.MapRotationServerAssignmentId, 0, 10, cancellationToken).ConfigureAwait(false);
                    if (opsResponse.IsSuccess && opsResponse.Result?.Data?.Items != null)
                    {
                        assignmentOperations[assignment.MapRotationServerAssignmentId] = [.. opsResponse.Result.Data.Items.OrderByDescending(o => o.StartedAt)];
                    }
                    else
                    {
                        this.AddAlertDanger("Operation history could not be loaded for an assignment. Recovery actions are disabled until its status can be checked.");
                    }
                }
            }

            // Determine if the user can assign to at least one compatible server (short-circuits on first match)
            var canAssignServer = await AnyAuthorizedServerAsync(rotation.GameType, cancellationToken).ConfigureAwait(false);

            return View(new MapRotationDetailsViewModel
            {
                Rotation = rotation,
                Maps = maps,
                AssignmentOperations = assignmentOperations,
                CanAssignServer = canAssignServer
            });
        }, nameof(Details)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (apiResponse.IsNotFound || apiResponse.Result?.Data is null)
                return NotFound();

            var rotation = apiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Delete),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            // Check for active assignments before attempting delete
            var activeAssignments = rotation.ServerAssignments?
                .Where(a => a.DeploymentState is not DeploymentState.Removed and not DeploymentState.Failed)
                .ToList() ?? [];

            if (activeAssignments.Count > 0)
            {
                this.AddAlertDanger("Cannot delete a map rotation that has active server assignments. Unassign all servers first.");
                return RedirectToAction(nameof(Details), new { id });
            }

            var deleteResponse = await repositoryApiClient.MapRotations.V1.DeleteMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (!deleteResponse.IsSuccess)
            {
                this.AddAlertDanger("An error occurred while deleting the map rotation.");
                return RedirectToAction(nameof(Details), new { id });
            }

            TrackSuccessTelemetry("MapRotationDeleted", nameof(Delete), new Dictionary<string, string>
            {
                { "MapRotationId", id.ToString() },
                { "Title", rotation.Title }
            });

            this.AddAlertSuccess($"Map rotation '{rotation.Title}' has been deleted.");

            return RedirectToAction(nameof(Index));
        }, nameof(Delete)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> CreateAssignment(Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authorizedServers = await GetAuthorizedServersAsync(rotation.GameType, cancellationToken).ConfigureAwait(false);

            if (authorizedServers.Count == 0)
                return Forbid();

            ViewData["RotationTitle"] = rotation.Title;

            return View(new CreateMapRotationAssignmentViewModel
            {
                MapRotationId = mapRotationId,
                AvailableServers = authorizedServers,
                CanBrowseServerFiles = authorizedServers.Any(server => server.FileTransportEnabled)
            });
        }, nameof(CreateAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAssignment(CreateMapRotationAssignmentViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(model.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            // Validate the submitted form before hitting the repository or authorization so that a
            // missing/invalid GameServerId redisplays the form with validation errors rather than a 404.
            var modelValidationResult = await CheckModelStateAsync(model, m => RepopulateAuthorizedServersAsync(m, rotation.GameType, cancellationToken)).ConfigureAwait(false);
            if (modelValidationResult != null)
            {
                ViewData["RotationTitle"] = rotation.Title;
                return modelValidationResult;
            }

            // Validate the posted server exists and belongs to the same game type
            var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(model.GameServerId, cancellationToken).ConfigureAwait(false);

            if (serverResponse.IsNotFound)
                return NotFound();

            // Distinguish a genuine 404 from an infrastructure/API failure: surface the latter as an
            // error rather than misclassifying it as NotFound (consistent with EnumerateCompatibleServersAsync).
            if (!serverResponse.IsSuccess || serverResponse.Result?.Data is null)
            {
                Logger.LogError(
                    "Failed to load game server {GameServerId} while creating a map rotation assignment for MapRotationId {MapRotationId}.",
                    model.GameServerId, model.MapRotationId);
                throw new InvalidOperationException($"Failed to load game server {model.GameServerId}.");
            }

            var server = serverResponse.Result.Data;

            if (!BaseAuthorizationHelper.AreGameTypesEquivalent(server.GameType, rotation.GameType))
            {
                ModelState.AddModelError(nameof(model.GameServerId), "The selected server does not match the rotation's game type.");
                await RepopulateAuthorizedServersAsync(model, rotation.GameType, cancellationToken).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            // Authorize against the exact (GameType, ServerId) tuple
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, model.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(CreateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var normalizedConfigVariableName = model.ConfigVariableName?.ToLowerInvariant();

            var hasExactDuplicateAssignment = rotation.ServerAssignments?.Any(a =>
                a.MapRotationId == model.MapRotationId &&
                a.GameServerId == model.GameServerId &&
                string.Equals(a.ConfigFilePath, model.ConfigFilePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ConfigVariableName, normalizedConfigVariableName, StringComparison.OrdinalIgnoreCase) &&
                a.DeploymentState != DeploymentState.Removed &&
                a.DeploymentState != DeploymentState.Removing &&
                a.DeploymentState != DeploymentState.Failed) == true;

            if (hasExactDuplicateAssignment)
            {
                ModelState.AddModelError(string.Empty, "An assignment already exists for this rotation and server with the same config file path and variable name.");
                await RepopulateAuthorizedServersAsync(model, rotation.GameType, cancellationToken).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            var createDto = new CreateMapRotationServerAssignmentDto(model.MapRotationId, model.GameServerId)
            {
                ConfigFilePath = model.ConfigFilePath,
                ConfigVariableName = normalizedConfigVariableName,
                PlayerCountMin = model.PlayerCountMin,
                PlayerCountMax = model.PlayerCountMax
            };

            ApiResult<MapRotationServerAssignmentDto> apiResponse;

            try
            {
                apiResponse = await repositoryApiClient.MapRotations.V1.CreateServerAssignment(createDto, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                Logger.LogWarning(ex,
                    "Validation failure creating map rotation assignment for MapRotationId {MapRotationId} and GameServerId {GameServerId}",
                    model.MapRotationId,
                    model.GameServerId);

                ModelState.AddModelError(string.Empty, "Unable to create server assignment. Please review the values and try again.");
                await RepopulateAuthorizedServersAsync(model, rotation.GameType, cancellationToken).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            if (!apiResponse.IsSuccess)
            {
                if (apiResponse.Result?.Errors != null)
                {
                    foreach (var error in apiResponse.Result.Errors)
                    {
                        ModelState.AddModelError(error.Target ?? string.Empty, error.Message ?? "An error occurred");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "An error occurred while creating the server assignment.");
                }

                await RepopulateAuthorizedServersAsync(model, rotation.GameType, cancellationToken).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            TrackSuccessTelemetry("MapRotationAssignmentCreated", nameof(CreateAssignment), new Dictionary<string, string>
            {
                { nameof(model.MapRotationId), model.MapRotationId.ToString() },
                { nameof(model.GameServerId), model.GameServerId.ToString() }
            });

            this.AddAlertSuccess("Server has been assigned to the map rotation.");

            return RedirectToAction(nameof(Details), new { id = model.MapRotationId });
        }, nameof(CreateAssignment)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> EditAssignment(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(id, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var assignment = assignmentResponse.Result.Data;

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignment.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(EditAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(assignment.GameServerId, cancellationToken).ConfigureAwait(false);
            var server = serverResponse.IsSuccess ? serverResponse.Result?.Data : null;

            var fileTransportType = server?.FileTransportType ?? FileTransportType.Unknown;

            ViewData["RotationTitle"] = rotation.Title;

            return View(new EditMapRotationAssignmentViewModel
            {
                MapRotationServerAssignmentId = assignment.MapRotationServerAssignmentId,
                MapRotationId = assignment.MapRotationId,
                GameServerId = assignment.GameServerId,
                GameServerTitle = server?.Title ?? assignment.GameServerId.ToString(),
                ConfigFilePath = assignment.ConfigFilePath,
                ConfigVariableName = assignment.ConfigVariableName,
                PlayerCountMin = assignment.PlayerCountMin,
                PlayerCountMax = assignment.PlayerCountMax,
                CanBrowseServerFiles = server?.FileTransportEnabled == true,
                FileTransportType = fileTransportType
            });
        }, nameof(EditAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAssignment(EditMapRotationAssignmentViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(model.MapRotationServerAssignmentId, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var assignment = assignmentResponse.Result.Data;

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignment.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(EditAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            async Task RepopulateAssignmentForm(EditMapRotationAssignmentViewModel m)
            {
                var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(assignment.GameServerId, cancellationToken).ConfigureAwait(false);
                var server = serverResponse.IsSuccess ? serverResponse.Result?.Data : null;

                m.GameServerTitle = server?.Title ?? assignment.GameServerId.ToString();

                m.CanBrowseServerFiles = server?.FileTransportEnabled == true;
                m.FileTransportType = server?.FileTransportType ?? FileTransportType.Unknown;
            }

            var modelValidationResult = await CheckModelStateAsync(model, RepopulateAssignmentForm).ConfigureAwait(false);
            if (modelValidationResult != null)
            {
                ViewData["RotationTitle"] = rotation.Title;
                return modelValidationResult;
            }

            var updateDto = new UpdateMapRotationServerAssignmentDto(model.MapRotationServerAssignmentId)
            {
                ConfigFilePath = model.ConfigFilePath,
                ConfigVariableName = model.ConfigVariableName?.ToLowerInvariant(),
                PlayerCountMin = model.PlayerCountMin,
                PlayerCountMax = model.PlayerCountMax
            };

            ApiResult apiResponse;

            try
            {
                apiResponse = await repositoryApiClient.MapRotations.V1.UpdateServerAssignment(updateDto, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                Logger.LogWarning(ex,
                    "Validation failure updating map rotation assignment {AssignmentId} for MapRotationId {MapRotationId}",
                    model.MapRotationServerAssignmentId,
                    model.MapRotationId);

                ModelState.AddModelError(string.Empty, "Unable to update server assignment. Please review the values and try again.");
                await RepopulateAssignmentForm(model).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            if (!apiResponse.IsSuccess)
            {
                if (apiResponse.Result?.Errors != null)
                {
                    foreach (var error in apiResponse.Result.Errors)
                    {
                        ModelState.AddModelError(error.Target ?? string.Empty, error.Message ?? "An error occurred");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Failed to update server assignment.");
                }

                await RepopulateAssignmentForm(model).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            this.AddAlertSuccess("Server assignment updated successfully.");

            TrackSuccessTelemetry("MapRotationAssignmentEdited", nameof(EditAssignment));

            return RedirectToAction(nameof(Details), new { id = model.MapRotationId });
        }, nameof(EditAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAssignment(Guid assignmentId, Guid mapRotationId, bool force = false, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(DeleteAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (assignment.DeploymentState == DeploymentState.Removed)
            {
                this.AddAlertInfo("This assignment is already unassigned. Removed assignments are retained for 90 days before automatic cleanup.");
                return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
            }

            if (assignment.ActivationState != ActivationState.Inactive)
                return BadRequest("Deactivate this assignment successfully before unassigning it. Server map files will be preserved.");

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;

            var result = await repositoryApiClient.MapRotations.V1.DeleteServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                this.AddAlertInfo("Assignment unassigned. Server map files were preserved. Removed assignments are retained for 90 days.");
                TrackSuccessTelemetry("MapRotationAssignmentDeleted", nameof(DeleteAssignment));
            }
            else
            {
                this.AddAlertDanger("Failed to unassign. Refresh the assignment and ensure deactivation and other operations have completed successfully.");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(DeleteAssignment)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> AssignmentStatus(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(id, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var assignment = assignmentResponse.Result.Data;

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignment.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            // Resource-based auth check against the rotation's game type
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.MapRotations_Read,
                nameof(AssignmentStatus),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var maps = new List<MapDto>();
            if (rotation.MapRotationMaps?.Count > 0)
            {
                var metadata = await mapMetadataService.GetAsync(
                    rotation.MapRotationMaps.Select(m => m.MapId), cancellationToken).ConfigureAwait(false);
                foreach (var rotationMap in rotation.MapRotationMaps.OrderBy(m => m.SortOrder))
                {
                    if (metadata.TryGetValue(rotationMap.MapId, out var map))
                        maps.Add(map);
                }
            }

            var opsResponse = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(id, 0, 20, cancellationToken).ConfigureAwait(false);
            if (!opsResponse.IsSuccess || opsResponse.Result?.Data?.Items == null)
                return StatusCode(502, "Unable to load assignment operations. Please retry before starting recovery.");
            List<MapRotationAssignmentOperationDto> operations = opsResponse.IsSuccess && opsResponse.Result?.Data?.Items != null
                ? [.. opsResponse.Result.Data.Items.OrderByDescending(o => o.StartedAt)]
                : [];

            var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(assignment.GameServerId, cancellationToken).ConfigureAwait(false);

            return View(new AssignmentStatusViewModel
            {
                Assignment = assignment,
                Rotation = rotation,
                GameServer = serverResponse.IsSuccess ? serverResponse.Result?.Data : null,
                Maps = maps,
                Operations = operations
            });
        }, nameof(AssignmentStatus)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var response = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);
            if (response.IsNotFound || response.Result?.Data is null)
                return NotFound();

            var rotation = response.Result.Data;
            var assignment = rotation.ServerAssignments?.FirstOrDefault(a => a.MapRotationServerAssignmentId == assignmentId);
            if (assignment == null)
                return BadRequest("The specified assignment does not belong to this rotation.");

            var authResult = await CheckAuthorizationAsync(authorizationService,
                (rotation.GameType, assignment.GameServerId), AuthPolicies.MapRotations_Deploy,
                nameof(ApplyAssignment), "MapRotation").ConfigureAwait(false);
            if (authResult != null)
                return authResult;

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;
            if (string.IsNullOrWhiteSpace(assignment.ConfigFilePath) || string.IsNullOrWhiteSpace(assignment.ConfigVariableName))
                return BadRequest("A configuration target is required to apply a rotation.");

            var result = await syncApiClient.TriggerApply(assignmentId, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                TempData[PendingInstanceKey] = $"maprot-apply-{assignmentId}";
                this.AddAlertInfo("Apply rotation queued: repair files and apply this slot and its linked main/AACP slot using the same rotation. Check each target's file and configuration results. No restart is performed and runtime use is not confirmed.");
                TrackSuccessTelemetry("MapRotationApplyQueued", nameof(ApplyAssignment));
            }
            else
            {
                this.AddAlertDanger($"Failed to queue Apply rotation: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(ApplyAssignment)).ConfigureAwait(false);
    }

    private async Task<IActionResult?> CheckOperationAvailable(MapRotationServerAssignmentDto assignment, CancellationToken cancellationToken)
    {
        if (assignment.DeploymentState is DeploymentState.Syncing or DeploymentState.Removing or DeploymentState.Removed
            || assignment.ActivationState is ActivationState.Activating or ActivationState.Deactivating)
            return Conflict("This assignment is busy or removed. Wait for the operation result or resolve cancellation before retrying.");

        var response = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(
            assignment.MapRotationServerAssignmentId, 0, 100, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items == null)
            return StatusCode(502, "Unable to check assignment operations. Please retry.");
        var hasOperationInProgress = response.Result.Data.Items.Any(o => o.Status == AssignmentOperationStatus.InProgress);
        return hasOperationInProgress
            ? Conflict("An assignment operation is still in progress. Wait for its result or cancel it successfully.")
            : (IActionResult?)null;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(SyncAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;

            var result = await syncApiClient.TriggerSync(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertInfo("File repair queued. Configuration is unchanged. Check the file operation result below.");
                TempData[PendingInstanceKey] = $"maprot-sync-{assignmentId}";
                TrackSuccessTelemetry("MapRotationAssignmentSynced", nameof(SyncAssignment));
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger sync: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(SyncAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(ActivateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;

            var result = await syncApiClient.TriggerActivate(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertInfo("Configuration application queued. Check the result below; runtime use is not confirmed. Restart only if required by the game/mod configuration, using a separate explicit action.");
                TempData[PendingInstanceKey] = $"maprot-activate-{assignmentId}";
                TrackSuccessTelemetry("MapRotationAssignmentActivated", nameof(ActivateAssignment));

                // Auto-promote rotation status to Published if still in Draft or Testing
                if (rotation.Status is MapRotationStatus.Draft or MapRotationStatus.Testing)
                {
                    var writeAuth = await authorizationService.AuthorizeAsync(User, rotation.GameType, AuthPolicies.MapRotations_Write).ConfigureAwait(false);
                    if (writeAuth.Succeeded)
                    {
                        // Re-read to avoid overwriting concurrent edits
                        var freshResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);
                        if (freshResponse.IsSuccess && freshResponse.Result?.Data is { } freshRotation
                            && freshRotation.Status is MapRotationStatus.Draft or MapRotationStatus.Testing
                            && freshRotation.GameType == rotation.GameType)
                        {
                            var promoteDto = new UpdateMapRotationDto(mapRotationId)
                            {
                                Title = freshRotation.Title,
                                Description = freshRotation.Description,
                                GameMode = freshRotation.GameMode,
                                Status = MapRotationStatus.Published,
                                Category = freshRotation.Category,
                                SequenceOrder = freshRotation.SequenceOrder,
                                LastModifiedByUserId = Guid.TryParse(User.UserProfileId(), out var upId) ? upId : null,
                                MapIds = freshRotation.MapRotationMaps?.OrderBy(m => m.SortOrder).Select(m => m.MapId).ToList() ?? []
                            };

                            var promoteResult = await repositoryApiClient.MapRotations.V1.UpdateMapRotation(promoteDto, cancellationToken).ConfigureAwait(false);
                            if (promoteResult.IsSuccess)
                            {
                                this.AddAlertInfo("Rotation status automatically promoted to Published.");
                            }
                            else
                            {
                                Logger.LogWarning("Failed to auto-promote rotation {RotationId} status to Published", mapRotationId);
                                this.AddAlertWarning("Configuration was queued, but the rotation status could not be promoted to Published.");
                            }
                        }
                    }
                }
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger activation: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(ActivateAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(DeactivateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;

            var result = await syncApiClient.TriggerDeactivate(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertInfo("Deactivation queued. Wait for successful completion before unassigning.");
                TempData[PendingInstanceKey] = $"maprot-deactivate-{assignmentId}";
                TrackSuccessTelemetry("MapRotationAssignmentDeactivated", nameof(DeactivateAssignment));
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger deactivation: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(DeactivateAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(VerifyAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var blocked = await CheckOperationAvailable(assignment, cancellationToken).ConfigureAwait(false);
            if (blocked != null)
                return blocked;

            var result = await syncApiClient.TriggerVerify(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertInfo("File verification queued. Check the result below; this does not confirm runtime use.");
                TempData[PendingInstanceKey] = $"maprot-verify-{assignmentId}";
                TrackSuccessTelemetry("MapRotationAssignmentVerified", nameof(VerifyAssignment));
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger verification: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(VerifyAssignment)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GetSyncProgress(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId) || instanceId.Length < 36
            || !Guid.TryParse(instanceId[^36..], out var assignmentId)
            || !IsAssignmentInstance(instanceId, assignmentId))
            return BadRequest("Invalid assignment instance ID.");

        var assignment = await repositoryApiClient.MapRotations.V1.GetServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);
        if (!assignment.IsSuccess || assignment.Result?.Data == null)
            return StatusCode(502, "Unable to resolve the assignment for progress.");
        var rotation = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignment.Result.Data.MapRotationId, cancellationToken).ConfigureAwait(false);
        if (!rotation.IsSuccess || rotation.Result?.Data == null)
            return StatusCode(502, "Unable to resolve the rotation for progress.");
        var authResult = await CheckAuthorizationAsync(authorizationService,
            rotation.Result.Data.GameType,
            AuthPolicies.MapRotations_Read, nameof(GetSyncProgress), "MapRotation").ConfigureAwait(false);
        if (authResult != null)
            return authResult;

        var queryResult = await syncApiClient.GetOrchestrationStatus(instanceId, cancellationToken).ConfigureAwait(false);
        return queryResult.Outcome switch
        {
            OrchestrationStatusQueryOutcome.Found => Json(new
            {
                status = "found",
                instanceId = queryResult.Result!.InstanceId,
                runtimeStatus = queryResult.Result.RuntimeStatus,
                createdAt = queryResult.Result.CreatedAt,
                lastUpdatedAt = queryResult.Result.LastUpdatedAt,
                progress = queryResult.Result.Progress,
                success = queryResult.Result.Success,
                error = queryResult.Result.Error ?? queryResult.Result.Progress?.Maps?.FirstOrDefault(m => m.Status == nameof(AssignmentOperationStatus.Failed))?.Error
            }),
            OrchestrationStatusQueryOutcome.NotFound => Json(new { status = "not_found" }),
            OrchestrationStatusQueryOutcome.Error => Json(new { status = "error" }),
            _ => Json(new { status = "error" })
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOperation(Guid operationId, Guid assignmentId, string? instanceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var assignment = assignmentResponse.Result.Data;

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignment.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotation.GameType, assignment.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(CancelOperation),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var operationsResponse = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(
                assignmentId, 0, 100, cancellationToken).ConfigureAwait(false);
            if (!operationsResponse.IsSuccess || operationsResponse.Result?.Data?.Items == null)
                return StatusCode(502, "Unable to verify operation ownership. Nothing was cancelled.");

            var operation = operationsResponse.Result.Data.Items.FirstOrDefault(o =>
                o.MapRotationAssignmentOperationId == operationId
                && o.MapRotationServerAssignmentId == assignmentId
                && string.Equals(o.DurableFunctionInstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (operation == null)
                return BadRequest("The operation and instance do not belong to this assignment.");
            if (!IsAssignmentInstance(instanceId, assignmentId)
                && !await IsLinkedApplyInstance(instanceId, assignment, cancellationToken).ConfigureAwait(false))
                return BadRequest("The instance does not belong to this assignment or its verified linked main/AACP slot.");
            var retryingFinalization = operation.Status is AssignmentOperationStatus.Cancelled or AssignmentOperationStatus.Failed
                && (assignment.DeploymentState is DeploymentState.Syncing or DeploymentState.Removing
                    || assignment.ActivationState is ActivationState.Activating or ActivationState.Deactivating);
            if (operation.Status != AssignmentOperationStatus.InProgress && !retryingFinalization)
                return Conflict("This operation is no longer in progress. Refresh its result.");
            if (retryingFinalization && operationsResponse.Result.Data.Items.Any(o => o.Status == AssignmentOperationStatus.InProgress))
                return Conflict("Another operation is in progress. Cancellation finalization cannot reset its assignment state.");
            if (retryingFinalization && operationsResponse.Result.Data.Items.Any(o => o.StartedAt > operation.StartedAt))
                return Conflict("A newer operation exists. Refresh its result before retrying cancellation finalization.");

            var (termination, alreadyFailed) = await TerminateAndConfirm(instanceId!, cancellationToken).ConfigureAwait(false);
            if (termination.CancellationRequested)
            {
                this.AddAlertInfo("Cancellation requested; waiting for the current transfer or configuration activity to finish safely. The operation is not yet cancelled.");
                TempData[PendingInstanceKey] = instanceId;
                return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
            }

            if (!termination.Success)
            {
                this.AddAlertDanger($"Cancellation failed; the operation was not marked cancelled: {termination.Error}");
                return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
            }

            if (!retryingFinalization)
            {
                var updateResult = await repositoryApiClient.MapRotations.V1.UpdateAssignmentOperation(
                    operationId, alreadyFailed ? AssignmentOperationStatus.Failed : AssignmentOperationStatus.Cancelled,
                    alreadyFailed ? "Orchestration failure confirmed during recovery" : "Orchestration terminated by user").ConfigureAwait(false);
                if (!updateResult.IsSuccess)
                {
                    this.AddAlertDanger("Orchestration is terminal, but its operation record could not be finalized. Refresh and retry cancellation/finalization.");
                    return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
                }
            }

            var refreshed = await repositoryApiClient.MapRotations.V1.GetServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);
            if (!refreshed.IsSuccess || refreshed.Result?.Data == null)
            {
                this.AddAlertDanger("Terminal status confirmed, but assignment state could not be reloaded. No reset was performed. Refresh and use Retry operation finalization if its state is still busy.");
                return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
            }

            assignment = refreshed.Result.Data;
            var deploymentReset = assignment.DeploymentState is DeploymentState.Syncing or DeploymentState.Removing
                ? DeploymentState.Failed
                : (DeploymentState?)null;
            var activationReset = assignment.ActivationState is ActivationState.Activating or ActivationState.Deactivating
                ? ActivationState.Failed
                : (ActivationState?)null;
            if (deploymentReset.HasValue || activationReset.HasValue)
            {
                var resetDto = new UpdateMapRotationServerAssignmentDto(assignmentId)
                {
                    DeploymentState = deploymentReset,
                    ActivationState = activationReset,
                    LastError = "Operation failed or was cancelled. Configuration may have changed; deactivate successfully before unassigning.",
                    LastErrorAt = DateTime.UtcNow
                };
                var resetResult = await repositoryApiClient.MapRotations.V1.UpdateServerAssignment(resetDto, cancellationToken).ConfigureAwait(false);
                if (!resetResult.IsSuccess)
                {
                    this.AddAlertDanger("Orchestration and operation are terminal, but assignment state reset failed. Refresh and use Retry operation finalization.");
                    return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
                }
            }

            if (alreadyFailed)
                this.AddAlertWarning("Failed operation finalized; assignment recovery is available. The operation did not succeed.");
            else
                this.AddAlertSuccess("Operation cancelled successfully.");
            TrackSuccessTelemetry(alreadyFailed ? "MapRotationFailedOperationFinalized" : "MapRotationOperationCancelled", nameof(CancelOperation));
            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(CancelOperation)).ConfigureAwait(false);
    }

    private static bool IsAssignmentInstance(string? instanceId, Guid assignmentId)
    {
        return assignmentOperationNames
            .Any(operation => string.Equals(instanceId, $"maprot-{operation}-{assignmentId}", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsLinkedApplyInstance(string? instanceId, MapRotationServerAssignmentDto assignment, CancellationToken cancellationToken)
    {
        const string prefix = "maprot-apply-";
        if (instanceId == null || !instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(instanceId[prefix.Length..], "D", out var initiatingAssignmentId))
            return false;

        var sourceResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(initiatingAssignmentId, cancellationToken).ConfigureAwait(false);
        return sourceResponse.IsSuccess && sourceResponse.Result?.Data is { } source && source.GameServerId == assignment.GameServerId
            && source.MapRotationId == assignment.MapRotationId
            && ((source.ConfigVariableName == "sv_maprotation" && assignment.ConfigVariableName == "scr_aacp_maps_1")
                || (source.ConfigVariableName == "scr_aacp_maps_1" && assignment.ConfigVariableName == "sv_maprotation"));
    }

    private async Task<(SyncTriggerResult Result, bool AlreadyFailed)> TerminateAndConfirm(string instanceId, CancellationToken cancellationToken)
    {
        var result = await syncApiClient.TerminateOrchestration(instanceId, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return (result, false);
        var status = await syncApiClient.GetOrchestrationStatus(instanceId, cancellationToken).ConfigureAwait(false);
        var runtimeStatus = status.Outcome == OrchestrationStatusQueryOutcome.Found ? status.Result?.RuntimeStatus : null;
        return runtimeStatus is "Terminated" or "Canceled" or nameof(AssignmentOperationStatus.Failed)
            ? (new SyncTriggerResult(true), runtimeStatus == nameof(AssignmentOperationStatus.Failed))
            : (new SyncTriggerResult(false, Error: "Termination was requested but is not confirmed. Refresh the operation result before retrying; no assignment reset was performed."), false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TerminateOrchestration(string instanceId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignmentResponse.Result.Data.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                (rotationResponse.Result.Data.GameType, assignmentResponse.Result.Data.GameServerId),
                AuthPolicies.MapRotations_Deploy,
                nameof(TerminateOrchestration),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (!IsAssignmentInstance(instanceId, assignmentId))
            {
                var operations = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(assignmentId, 0, 100, cancellationToken).ConfigureAwait(false);
                if (!operations.IsSuccess || operations.Result?.Data?.Items == null)
                    return StatusCode(502, "Unable to verify operation ownership. Nothing was cancelled.");
                var persisted = operations.Result.Data.Items.Any(o => o.MapRotationServerAssignmentId == assignmentId
                    && string.Equals(o.DurableFunctionInstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
                if (!persisted || !await IsLinkedApplyInstance(instanceId, assignmentResponse.Result.Data, cancellationToken).ConfigureAwait(false))
                    return BadRequest("The instance does not belong to this assignment or its verified linked main/AACP slot.");
            }

            var (terminateResult, alreadyFailed) = await TerminateAndConfirm(instanceId, cancellationToken).ConfigureAwait(false);

            if (terminateResult.CancellationRequested)
            {
                this.AddAlertInfo("Cancellation requested; waiting for the current transfer or configuration activity to finish safely. The operation is not yet cancelled.");
                TempData[PendingInstanceKey] = instanceId;
            }
            else if (terminateResult.Success && alreadyFailed)
            {
                this.AddAlertWarning("Orchestration has already failed. Use its operation cancellation/finalization action if the assignment is still busy; failure is not cancellation success.");
            }
            else if (terminateResult.Success)
            {
                this.AddAlertInfo($"Orchestration '{instanceId}' termination confirmed. Check the operation and assignment states before retrying.");
                TrackSuccessTelemetry("MapRotationOrchestrationTerminated", nameof(TerminateOrchestration));
            }
            else
            {
                this.AddAlertDanger($"Failed to terminate orchestration '{instanceId}': {terminateResult.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(TerminateOrchestration)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        var canImport = await authorizationService.AuthorizeAsync(User, PotentialAccessProbe.Instance, AuthPolicies.MapRotations_Write).ConfigureAwait(false);
        return !canImport.Succeeded ? Forbid() : View(new ImportMapRotationsViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(ImportMapRotationsViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!supportedGameTypes.Contains(model.GameType))
            {
                ModelState.AddModelError(nameof(model.GameType), "Only Call of Duty 4, Call of Duty 4x, and Call of Duty 5 are supported.");
                return View(model);
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                model.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(Import),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            // Read cfg content from textarea or uploaded file
            var cfgContent = model.CfgContent;
            if (model.CfgFile is { Length: > 0 })
            {
                if (model.CfgFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(model.CfgFile), "Config file must be under 1 MB.");
                    return View(model);
                }

                using var reader = new StreamReader(model.CfgFile.OpenReadStream());
                cfgContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(cfgContent))
            {
                ModelState.AddModelError("", "Please paste config content or upload a .cfg file.");
                return View(model);
            }

            // Parse the cfg content
            var parsed = MapRotationCfgParser.Parse(cfgContent);

            if (parsed.Count == 0)
            {
                ModelState.AddModelError("", "No map rotations found in the config content. Expected lines like: set sv_maprotation \"gametype ftag map mp_xxx map mp_yyy\"");
                return View(model);
            }

            // Collect all unique map names for duplicate/new-map detection
            var allMapNames = parsed.SelectMany(r => r.MapNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var existingMaps = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            if (allMapNames.Length > 0)
            {
                // Bulk lookup existing maps (paginate if needed)
                for (var skip = 0; skip < allMapNames.Length; skip += 100)
                {
                    var batch = allMapNames.Skip(skip).Take(100).ToArray();
                    var mapsResponse = await repositoryApiClient.Maps.V1.GetMaps(
                        model.GameType, batch, null, null, 0, 100, null, cancellationToken).ConfigureAwait(false);

                    if (mapsResponse.IsSuccess && mapsResponse.Result?.Data?.Items != null)
                    {
                        foreach (var map in mapsResponse.Result.Data.Items)
                        {
                            if (map.MapName != null && !existingMaps.ContainsKey(map.MapName))
                                existingMaps[map.MapName] = map.MapId;
                        }
                    }
                }
            }

            var newMapNames = allMapNames.Where(m => !existingMaps.ContainsKey(m)).OrderBy(m => m).ToList();

            // Check for existing rotations with matching titles for duplicate detection (best-effort, capped at 200)
            var existingRotations = await repositoryApiClient.MapRotations.V1.GetMapRotations(
                [model.GameType], null, null, 0, 200, MapRotationsOrder.TitleAsc, cancellationToken).ConfigureAwait(false);

            var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingRotations.IsSuccess && existingRotations.Result?.Data?.Items != null)
            {
                foreach (var r in existingRotations.Result.Data.Items)
                    existingTitles.Add(r.Title);
            }

            // Build preview items
            var previewItems = new List<ImportRotationPreviewItem>();
            var warnings = new List<string>();

            for (var i = 0; i < parsed.Count; i++)
            {
                var rotation = parsed[i];
                var isDuplicate = existingTitles.Contains(rotation.Title);

                previewItems.Add(new ImportRotationPreviewItem
                {
                    Index = i,
                    Title = rotation.Title,
                    Description = rotation.RawComment ?? "",
                    GameMode = rotation.GameMode,
                    MapCount = rotation.MapNames.Count,
                    MapNames = rotation.MapNames,
                    ConfigVariableName = rotation.ConfigVariableName,
                    IsActive = rotation.IsActive,
                    Author = rotation.Author,
                    DateText = rotation.DateText,
                    Selected = !isDuplicate,
                    IsDuplicate = isDuplicate,
                    DuplicateWarning = isDuplicate ? $"A rotation named \"{rotation.Title}\" already exists" : null
                });
            }

            if (newMapNames.Count > 0)
            {
                warnings.Add($"{newMapNames.Count} new map(s) will be created: {string.Join(", ", newMapNames.Take(20))}{(newMapNames.Count > 20 ? $" and {newMapNames.Count - 20} more..." : "")}");
            }

            // Store parsed data server-side via memory cache (TempData is cookie-based and too small for large imports)
            var draftId = Guid.NewGuid().ToString("N");
            var cacheKey = $"ImportDraft_{draftId}";
            memoryCache.Set(cacheKey, JsonConvert.SerializeObject(new
            {
                GameType = model.GameType,
                Rotations = parsed,
                NewMapNames = newMapNames
            }), TimeSpan.FromMinutes(15));

            TrackSuccessTelemetry("MapRotationImported", nameof(Import));

            return View("ImportPreview", new ImportMapRotationsPreviewViewModel
            {
                GameType = model.GameType,
                Rotations = previewItems,
                NewMapNames = newMapNames,
                Warnings = warnings,
                DraftId = draftId
            });
        }, nameof(Import)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportConfirm(ImportMapRotationsConfirmViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            // Retrieve the server-side draft from memory cache
            var cacheKey = $"ImportDraft_{model.DraftId}";
            var draftJson = memoryCache.Get<string>(cacheKey);
            if (string.IsNullOrEmpty(draftJson))
            {
                this.AddAlertDanger("Import session expired. Please start the import again.");
                return RedirectToAction(nameof(Import));
            }

            // Remove draft after retrieval (one-time use)
            memoryCache.Remove(cacheKey);

            var draft = JsonConvert.DeserializeAnonymousType(draftJson, new
            {
                GameType = default(GameType),
                Rotations = new List<ParsedRotation>(),
                NewMapNames = new List<string>()
            })!;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                draft.GameType,
                AuthPolicies.MapRotations_Write,
                nameof(ImportConfirm),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var selectedIndices = new HashSet<int>(model.SelectedIndices);
            var editLookup = model.Edits
                .Where(e => e != null)
                .ToDictionary(e => e.Index, e => e);
            var prefix = model.ImportPrefix?.Trim() ?? "";
            var selectedRotationsWithIndex = draft.Rotations
                .Select((r, i) => (Rotation: r, Index: i))
                .Where(x => selectedIndices.Contains(x.Index))
                .ToList();

            if (selectedRotationsWithIndex.Count == 0)
            {
                this.AddAlertWarning("No rotations selected for import.");
                return RedirectToAction(nameof(Import));
            }

            // Step 1: Create missing maps (batch, best-effort — may fail on concurrent imports)
            var mapsCreated = 0;
            if (draft.NewMapNames.Count > 0)
            {
                try
                {
                    var mapDtos = draft.NewMapNames
                        .Select(name => new CreateMapDto(draft.GameType, name))
                        .ToList();

                    var createResult = await repositoryApiClient.Maps.V1.CreateMaps(mapDtos, cancellationToken).ConfigureAwait(false);
                    if (createResult.IsSuccess)
                        mapsCreated = mapDtos.Count;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Bulk map creation failed (maps may already exist from concurrent import)");
                }
            }

            // Step 2: Resolve ALL map names to GUIDs (always re-resolve after creation attempt)
            var allMapNames = selectedRotationsWithIndex.SelectMany(x => x.Rotation.MapNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var mapLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            for (var skip = 0; skip < allMapNames.Length; skip += 100)
            {
                var batch = allMapNames.Skip(skip).Take(100).ToArray();
                var mapsResponse = await repositoryApiClient.Maps.V1.GetMaps(
                    draft.GameType, batch, null, null, 0, 100, null, cancellationToken).ConfigureAwait(false);

                if (mapsResponse.IsSuccess && mapsResponse.Result?.Data?.Items != null)
                {
                    foreach (var map in mapsResponse.Result.Data.Items)
                    {
                        if (map.MapName != null && !mapLookup.ContainsKey(map.MapName))
                            mapLookup[map.MapName] = map.MapId;
                    }
                }
            }

            // Step 3: Create rotations
            var results = new List<ImportResultItem>();

            foreach (var (rotation, rotationIndex) in selectedRotationsWithIndex)
            {
                try
                {
                    var mapIds = rotation.MapNames
                        .Where(m => mapLookup.ContainsKey(m))
                        .Select(m => mapLookup[m])
                        .ToList();

                    var unresolvedMaps = rotation.MapNames.Where(m => !mapLookup.ContainsKey(m)).ToList();
                    if (unresolvedMaps.Count > 0)
                    {
                        results.Add(new ImportResultItem
                        {
                            Title = rotation.Title,
                            Status = "Failed",
                            Error = $"Could not resolve maps: {string.Join(", ", unresolvedMaps)}"
                        });
                        continue;
                    }

                    // Apply user edits (title/description) and prefix
                    var title = rotation.Title;
                    var description = rotation.RawComment;
                    if (editLookup.TryGetValue(rotationIndex, out var edit))
                    {
                        if (!string.IsNullOrWhiteSpace(edit.Title))
                            title = edit.Title.Trim();
                        description = edit.Description?.Trim();
                    }

                    if (!string.IsNullOrEmpty(prefix) && !title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        title = $"{prefix} — {title}";
                    }

                    var createDto = new CreateMapRotationDto(draft.GameType, title, rotation.GameMode)
                    {
                        Description = description,
                        CreatedByUserId = Guid.TryParse(User.UserProfileId(), out var importUpId) ? importUpId : null,
                        MapIds = mapIds
                    };

                    var createResult = await repositoryApiClient.MapRotations.V1
                        .CreateMapRotation(createDto, cancellationToken).ConfigureAwait(false);

                    if (createResult.IsSuccess && createResult.Result?.Data != null)
                    {
                        results.Add(new ImportResultItem
                        {
                            Title = title,
                            Status = "Imported",
                            MapRotationId = createResult.Result.Data.MapRotationId
                        });
                    }
                    else
                    {
                        results.Add(new ImportResultItem
                        {
                            Title = title,
                            Status = "Failed",
                            Error = "API returned failure"
                        });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new ImportResultItem
                    {
                        Title = rotation.Title,
                        Status = "Failed",
                        Error = ex.Message
                    });
                }
            }

            TrackSuccessTelemetry("MapRotationImportConfirmed", nameof(ImportConfirm));

            return View("ImportResult", new ImportMapRotationsResultViewModel
            {
                GameType = draft.GameType,
                ImportedCount = results.Count(r => r.Status == "Imported"),
                SkippedCount = selectedRotationsWithIndex.Count - results.Count,
                FailedCount = results.Count(r => r.Status == "Failed"),
                MapsCreatedCount = mapsCreated,
                Results = results
            });
        }, nameof(ImportConfirm)).ConfigureAwait(false);
    }

    /// <summary>
    /// Pages through all game servers compatible with the specified game type, yielding each server.
    /// Surfaces repository failures as an exception rather than silently ending the sequence, so that
    /// callers do not misclassify an infrastructure failure as "no servers". Enumeration is lazy, so
    /// callers that stop early (e.g. after the first authorized server) avoid fetching later pages.
    /// </summary>
    private async IAsyncEnumerable<Repository.Abstractions.Models.V1.GameServers.GameServerDto> EnumerateCompatibleServersAsync(
        GameType gameType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var equivalentGameTypes = BaseAuthorizationHelper.GetEquivalentGameTypes(gameType).ToArray();
        const int pageSize = 100;
        var offset = 0;

        while (true)
        {
            var serversResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                equivalentGameTypes, null, null, offset, pageSize, null, cancellationToken).ConfigureAwait(false);

            // Surface repository failures — including a successful-but-malformed response with no
            // Data/Items payload — as errors rather than silently treating them as "no servers"
            // (which would misclassify an infrastructure failure as a Forbid / hidden UI).
            // A genuine empty result returns a non-null, empty Items collection and is handled below.
            if (!serversResponse.IsSuccess || serversResponse.Result?.Data?.Items is null)
            {
                Logger.LogError(
                    "Failed to load game servers for game type {GameType} while resolving authorized deploy servers (offset {Offset}).",
                    gameType, offset);
                throw new InvalidOperationException($"Failed to load game servers for game type {gameType}.");
            }

            var serverBatch = serversResponse.Result.Data.Items.ToList();

            foreach (var server in serverBatch)
                yield return server;

            if (serverBatch.Count < pageSize)
                yield break;

            offset += pageSize;
        }
    }

    /// <summary>
    /// Loads all compatible game servers and filters to only those the current user is authorized to deploy to.
    /// </summary>
    private async Task<List<Repository.Abstractions.Models.V1.GameServers.GameServerDto>> GetAuthorizedServersAsync(
        GameType gameType, CancellationToken cancellationToken)
    {
        var authorizedServers = new List<Repository.Abstractions.Models.V1.GameServers.GameServerDto>();

        await foreach (var server in EnumerateCompatibleServersAsync(gameType, cancellationToken).ConfigureAwait(false))
        {
            var deployAuth = await authorizationService.AuthorizeAsync(
                User, (gameType, server.GameServerId), AuthPolicies.MapRotations_Deploy).ConfigureAwait(false);

            if (deployAuth.Succeeded)
                authorizedServers.Add(server);
        }

        return authorizedServers;
    }

    /// <summary>
    /// Determines whether the current user is authorized to deploy to at least one compatible server.
    /// Short-circuits on the first authorized server, avoiding a full-fleet authorization sweep (and any
    /// later page fetches). Repository failures surface as an exception rather than "no access".
    /// </summary>
    private async Task<bool> AnyAuthorizedServerAsync(GameType gameType, CancellationToken cancellationToken)
    {
        await foreach (var server in EnumerateCompatibleServersAsync(gameType, cancellationToken).ConfigureAwait(false))
        {
            var deployAuth = await authorizationService.AuthorizeAsync(
                User, (gameType, server.GameServerId), AuthPolicies.MapRotations_Deploy).ConfigureAwait(false);

            if (deployAuth.Succeeded)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Repopulates the AvailableServers on a CreateMapRotationAssignmentViewModel with only authorized servers.
    /// </summary>
    private async Task RepopulateAuthorizedServersAsync(
        CreateMapRotationAssignmentViewModel model, GameType gameType, CancellationToken cancellationToken)
    {
        model.AvailableServers = await GetAuthorizedServersAsync(gameType, cancellationToken).ConfigureAwait(false);

        model.CanBrowseServerFiles = model.AvailableServers.Any(server => server.FileTransportEnabled);
    }
}
