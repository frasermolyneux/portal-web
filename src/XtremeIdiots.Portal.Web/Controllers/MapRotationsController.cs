using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.AccessMapRotations)]
public class MapRotationsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    ISyncApiClient syncApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapRotationsController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    private readonly static GameType[] supportedGameTypes = [GameType.CallOfDuty4, GameType.CallOfDuty5];

    private async Task PopulateInitialMapsViewBag(List<Guid> mapIds, CancellationToken cancellationToken = default)
    {
        var initialMaps = new List<object>();
        foreach (var mapId in mapIds)
        {
            var mapResponse = await repositoryApiClient.Maps.V1.GetMap(mapId, cancellationToken).ConfigureAwait(false);
            if (mapResponse.IsSuccess && mapResponse.Result?.Data != null)
            {
                var map = mapResponse.Result.Data;
                initialMaps.Add(new { id = map.MapId.ToString(), text = map.MapName, imageUrl = !string.IsNullOrEmpty(map.MapImageUri) ? map.MapImageUri : "/images/noimage.jpg" });
            }
        }
        ViewBag.InitialMaps = System.Text.Json.JsonSerializer.Serialize(initialMaps);
    }

    [HttpGet]
    public async Task<IActionResult> Index(GameType? gameType, CancellationToken cancellationToken = default)
    {
        if (gameType.HasValue && !supportedGameTypes.Contains(gameType.Value))
            return BadRequest("Only Call of Duty 4 and Call of Duty 5 are supported.");

        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameTypes = gameType.HasValue ? [gameType.Value] : Array.Empty<GameType>();

            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotations(
                gameTypes, null, null, 0, 100, MapRotationsOrder.TitleAsc, cancellationToken).ConfigureAwait(false);

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
    public IActionResult Create()
    {
        return View(new CreateMapRotationViewModel
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
            ModelState.AddModelError(nameof(model.GameType), "Only Call of Duty 4 and Call of Duty 5 are supported.");
            await PopulateInitialMapsViewBag(model.MapIds, cancellationToken).ConfigureAwait(false);
            return View(model);
        }

        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                model.GameType,
                AuthPolicies.CreateMapRotation,
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

            TrackSuccessTelemetry(nameof(Create), "MapRotationCreated", new Dictionary<string, string>
            {
                { nameof(model.GameType), model.GameType.ToString() },
                { nameof(model.Title), model.Title }
            });

            this.AddAlertSuccess($"Map rotation '{model.Title}' has been created successfully.");

            var createdId = apiResponse.Result?.Data?.MapRotationId;
            if (createdId.HasValue && createdId.Value != Guid.Empty)
                return RedirectToAction(nameof(Details), new { id = createdId.Value });

            return RedirectToAction(nameof(Index));
        }, nameof(Create)).ConfigureAwait(false);
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
                AuthPolicies.EditMapRotation,
                nameof(Edit),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var initialMaps = new List<object>();
            if (rotation.MapRotationMaps?.Count > 0)
            {
                foreach (var rotationMap in rotation.MapRotationMaps.OrderBy(m => m.SortOrder))
                {
                    var mapResponse = await repositoryApiClient.Maps.V1.GetMap(rotationMap.MapId, cancellationToken).ConfigureAwait(false);
                    if (mapResponse.IsSuccess && mapResponse.Result?.Data != null)
                    {
                        var map = mapResponse.Result.Data;
                        initialMaps.Add(new { id = map.MapId.ToString(), text = map.MapName, imageUrl = !string.IsNullOrEmpty(map.MapImageUri) ? map.MapImageUri : "/images/noimage.jpg" });
                    }
                }
            }
            ViewBag.InitialMaps = System.Text.Json.JsonSerializer.Serialize(initialMaps);

            return View(new EditMapRotationViewModel
            {
                MapRotationId = rotation.MapRotationId,
                Title = rotation.Title,
                Description = rotation.Description,
                GameMode = rotation.GameMode,
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
                AuthPolicies.EditMapRotation,
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

            TrackSuccessTelemetry(nameof(Edit), "MapRotationUpdated", new Dictionary<string, string>
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

            // Load map details for each map in the rotation
            var maps = new List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps.MapDto>();
            if (rotation.MapRotationMaps?.Count > 0)
            {
                foreach (var rotationMap in rotation.MapRotationMaps.OrderBy(m => m.SortOrder))
                {
                    var mapResponse = await repositoryApiClient.Maps.V1.GetMap(rotationMap.MapId, cancellationToken).ConfigureAwait(false);
                    if (mapResponse.IsSuccess && mapResponse.Result?.Data != null)
                    {
                        maps.Add(mapResponse.Result.Data);
                    }
                }
            }

            // Load game server details and operations for each assignment
            var assignmentOperations = new Dictionary<Guid, List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations.MapRotationAssignmentOperationDto>>();
            if (rotation.ServerAssignments?.Count > 0)
            {
                foreach (var assignment in rotation.ServerAssignments)
                {
                    var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(assignment.GameServerId, cancellationToken).ConfigureAwait(false);
                    if (serverResponse.IsSuccess && serverResponse.Result?.Data != null)
                    {
                        ViewData[$"Server_{assignment.GameServerId}"] = serverResponse.Result.Data;
                    }

                    var opsResponse = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(assignment.MapRotationServerAssignmentId, 0, 10, cancellationToken).ConfigureAwait(false);
                    if (opsResponse.IsSuccess && opsResponse.Result?.Data?.Items != null)
                    {
                        assignmentOperations[assignment.MapRotationServerAssignmentId] = [.. opsResponse.Result.Data.Items.OrderByDescending(o => o.StartedAt)];
                    }
                }
            }

            return View(new MapRotationDetailsViewModel
            {
                Rotation = rotation,
                Maps = maps,
                AssignmentOperations = assignmentOperations
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
                AuthPolicies.DeleteMapRotation,
                nameof(Delete),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var deleteResponse = await repositoryApiClient.MapRotations.V1.DeleteMapRotation(id, cancellationToken).ConfigureAwait(false);

            if (!deleteResponse.IsSuccess)
            {
                this.AddAlertDanger("An error occurred while deleting the map rotation.");
                return RedirectToAction(nameof(Details), new { id });
            }

            TrackSuccessTelemetry(nameof(Delete), "MapRotationDeleted", new Dictionary<string, string>
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

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(CreateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var serversResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                [rotation.GameType], null, null, 0, 100, null, cancellationToken).ConfigureAwait(false);

            List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers.GameServerDto> servers = serversResponse.IsSuccess && serversResponse.Result?.Data?.Items != null
                ? [.. serversResponse.Result.Data.Items]
                : [];

            ViewData["RotationTitle"] = rotation.Title;

            return View(new CreateMapRotationAssignmentViewModel
            {
                MapRotationId = mapRotationId,
                AvailableServers = servers
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

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(CreateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            // Re-populate servers for validation failure
            async Task RepopulateServers(CreateMapRotationAssignmentViewModel m)
            {
                var serversResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                    [rotation.GameType], null, null, 0, 100, null, cancellationToken).ConfigureAwait(false);
                List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers.GameServerDto> serverList = serversResponse.IsSuccess && serversResponse.Result?.Data?.Items != null
                    ? [.. serversResponse.Result.Data.Items]
                    : [];
                m.AvailableServers = serverList;
            }

            var modelValidationResult = await CheckModelStateAsync(model, RepopulateServers).ConfigureAwait(false);
            if (modelValidationResult != null)
            {
                ViewData["RotationTitle"] = rotation.Title;
                return modelValidationResult;
            }

            var createDto = new CreateMapRotationServerAssignmentDto(model.MapRotationId, model.GameServerId)
            {
                ConfigFilePath = model.ConfigFilePath,
                ConfigVariableName = model.ConfigVariableName
            };

            var apiResponse = await repositoryApiClient.MapRotations.V1.CreateServerAssignment(createDto, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess)
            {
                this.AddAlertDanger("An error occurred while creating the server assignment.");
                await RepopulateServers(model).ConfigureAwait(false);
                ViewData["RotationTitle"] = rotation.Title;
                return View(model);
            }

            TrackSuccessTelemetry(nameof(CreateAssignment), "MapRotationAssignmentCreated", new Dictionary<string, string>
            {
                { nameof(model.MapRotationId), model.MapRotationId.ToString() },
                { nameof(model.GameServerId), model.GameServerId.ToString() }
            });

            this.AddAlertSuccess("Server has been assigned to the map rotation.");

            return RedirectToAction(nameof(Details), new { id = model.MapRotationId });
        }, nameof(CreateAssignment)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(DeleteAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            // Verify the assignment belongs to this rotation
            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
            {
                return BadRequest("The specified assignment does not belong to this rotation.");
            }

            var deleteResponse = await repositoryApiClient.MapRotations.V1.DeleteServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

            if (!deleteResponse.IsSuccess)
            {
                this.AddAlertDanger("An error occurred while removing the server assignment.");
            }
            else
            {
                TrackSuccessTelemetry(nameof(DeleteAssignment), "MapRotationAssignmentDeleted", new Dictionary<string, string>
                {
                    { "AssignmentId", assignmentId.ToString() },
                    { "MapRotationId", mapRotationId.ToString() }
                });

                this.AddAlertSuccess("Server assignment has been removed.");
            }

            return RedirectToAction(nameof(Details), new { id = mapRotationId });
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
                AuthPolicies.AccessMapRotations,
                nameof(AssignmentStatus),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var maps = new List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps.MapDto>();
            if (rotation.MapRotationMaps?.Count > 0)
            {
                foreach (var rotationMap in rotation.MapRotationMaps.OrderBy(m => m.SortOrder))
                {
                    var mapResponse = await repositoryApiClient.Maps.V1.GetMap(rotationMap.MapId, cancellationToken).ConfigureAwait(false);
                    if (mapResponse.IsSuccess && mapResponse.Result?.Data != null)
                    {
                        maps.Add(mapResponse.Result.Data);
                    }
                }
            }

            var opsResponse = await repositoryApiClient.MapRotations.V1.GetAssignmentOperations(id, 0, 20, cancellationToken).ConfigureAwait(false);
            List<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations.MapRotationAssignmentOperationDto> operations = opsResponse.IsSuccess && opsResponse.Result?.Data?.Items != null
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
    public async Task<IActionResult> SyncAssignment(Guid assignmentId, Guid mapRotationId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(mapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var rotation = rotationResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(SyncAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var result = await syncApiClient.TriggerSync(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertSuccess("Sync triggered successfully.");
                TempData["PendingInstanceId"] = $"maprot-sync-{assignmentId}";
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

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(ActivateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var result = await syncApiClient.TriggerActivate(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertSuccess("Activation triggered successfully.");
                TempData["PendingInstanceId"] = $"maprot-activate-{assignmentId}";
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

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(DeactivateAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var result = await syncApiClient.TriggerDeactivate(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertSuccess("Deactivation triggered successfully.");
                TempData["PendingInstanceId"] = $"maprot-deactivate-{assignmentId}";
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger deactivation: {result.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(DeactivateAssignment)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GetSyncProgress(string instanceId, CancellationToken cancellationToken = default)
    {
        var queryResult = await syncApiClient.GetOrchestrationStatus(instanceId, cancellationToken).ConfigureAwait(false);
        return queryResult.Outcome switch
        {
            Services.OrchestrationStatusQueryOutcome.Found => Json(new
            {
                status = "found",
                instanceId = queryResult.Result!.InstanceId,
                runtimeStatus = queryResult.Result.RuntimeStatus,
                createdAt = queryResult.Result.CreatedAt,
                lastUpdatedAt = queryResult.Result.LastUpdatedAt,
                progress = queryResult.Result.Progress
            }),
            Services.OrchestrationStatusQueryOutcome.NotFound => Json(new { status = "not_found" }),
            Services.OrchestrationStatusQueryOutcome.Error => Json(new { status = "error" }),
            _ => Json(new { status = "error" })
        };
    }
}
