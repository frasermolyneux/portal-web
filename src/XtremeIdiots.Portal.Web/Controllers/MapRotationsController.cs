using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
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
    IMemoryCache memoryCache,
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
                AuthPolicies.CreateMapRotation,
                nameof(Clone),
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
                Status = model.Status,
                Category = model.Category,
                SequenceOrder = model.SequenceOrder,
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

            var canBrowseFtp = await authorizationService.AuthorizeAsync(User, rotation.GameType, AuthPolicies.EditGameServerFtp).ConfigureAwait(false);

            return View(new CreateMapRotationAssignmentViewModel
            {
                MapRotationId = mapRotationId,
                AvailableServers = servers,
                CanBrowseFtp = canBrowseFtp.Succeeded
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

                var canBrowseFtp = await authorizationService.AuthorizeAsync(User, rotation.GameType, AuthPolicies.EditGameServerFtp).ConfigureAwait(false);
                m.CanBrowseFtp = canBrowseFtp.Succeeded;
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
                ConfigVariableName = model.ConfigVariableName,
                PlayerCountMin = model.PlayerCountMin,
                PlayerCountMax = model.PlayerCountMax
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

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var assignment = rotation.ServerAssignments.First(a => a.MapRotationServerAssignmentId == assignmentId);

            // If already removed, delete the DB record directly
            if (assignment.DeploymentState == DeploymentState.Removed)
            {
                var deleteResponse = await repositoryApiClient.MapRotations.V1.DeleteServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

                if (!deleteResponse.IsSuccess)
                {
                    this.AddAlertDanger("An error occurred while removing the server assignment.");
                }
                else
                {
                    this.AddAlertSuccess("Server assignment has been removed.");
                }

                return RedirectToAction(nameof(Details), new { id = mapRotationId });
            }

            // Trigger the Remove orchestration to clean up maps from the server
            var result = await syncApiClient.TriggerRemove(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertSuccess("Unassign triggered. Maps are being removed from the server.");
                TempData["PendingInstanceId"] = $"maprot-remove-{assignmentId}";
            }
            else
            {
                this.AddAlertDanger($"Failed to trigger unassign: {result.Error}");
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

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(VerifyAssignment),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            if (rotation.ServerAssignments == null || !rotation.ServerAssignments.Any(a => a.MapRotationServerAssignmentId == assignmentId))
                return BadRequest("The specified assignment does not belong to this rotation.");

            var result = await syncApiClient.TriggerVerify(assignmentId, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                this.AddAlertSuccess("Verification triggered successfully.");
                TempData["PendingInstanceId"] = $"maprot-verify-{assignmentId}";
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
                rotation.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(CancelOperation),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var updateResult = await repositoryApiClient.MapRotations.V1.UpdateAssignmentOperation(
                operationId, AssignmentOperationStatus.Cancelled, "Manually cancelled by user").ConfigureAwait(false);

            if (updateResult.IsSuccess)
            {
                // Terminate the durable function orchestration so a new one can be started
                if (!string.IsNullOrEmpty(instanceId))
                {
                    // Validate the instance ID belongs to this assignment
                    var allowedPrefixes = new[]
                    {
                        $"maprot-sync-{assignmentId}",
                        $"maprot-activate-{assignmentId}",
                        $"maprot-deactivate-{assignmentId}",
                        $"maprot-remove-{assignmentId}",
                        $"maprot-verify-{assignmentId}"
                    };

                    if (allowedPrefixes.Any(p => string.Equals(instanceId, p, StringComparison.OrdinalIgnoreCase)))
                    {
                        await syncApiClient.TerminateOrchestration(instanceId, cancellationToken).ConfigureAwait(false);
                    }
                }

                // Also reset the assignment state so the user can retry
                var deploymentReset = assignment.DeploymentState is DeploymentState.Syncing or DeploymentState.Removing
                    ? DeploymentState.Failed
                    : (DeploymentState?)null;

                var activationReset = assignment.ActivationState is ActivationState.Activating or ActivationState.Deactivating
                    ? ActivationState.Inactive
                    : (ActivationState?)null;

                if (deploymentReset.HasValue || activationReset.HasValue)
                {
                    var resetDto = new UpdateMapRotationServerAssignmentDto(assignmentId)
                    {
                        DeploymentState = deploymentReset,
                        ActivationState = activationReset,
                        LastError = "Operation cancelled by user",
                        LastErrorAt = DateTime.UtcNow
                    };

                    await repositoryApiClient.MapRotations.V1.UpdateServerAssignment(resetDto, cancellationToken).ConfigureAwait(false);
                }

                this.AddAlertSuccess("Operation cancelled successfully.");
            }
            else
            {
                this.AddAlertDanger("Failed to cancel operation. Please try again.");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(CancelOperation)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TerminateOrchestration(string instanceId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            // Validate the instance ID belongs to this assignment
            var allowedPrefixes = new[]
            {
                $"maprot-sync-{assignmentId}",
                $"maprot-activate-{assignmentId}",
                $"maprot-deactivate-{assignmentId}",
                $"maprot-remove-{assignmentId}",
                $"maprot-verify-{assignmentId}"
            };

            if (!allowedPrefixes.Any(p => string.Equals(instanceId, p, StringComparison.OrdinalIgnoreCase)))
                return BadRequest("The instance ID does not belong to the specified assignment.");

            var assignmentResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignment(assignmentId, cancellationToken).ConfigureAwait(false);

            if (assignmentResponse.IsNotFound || assignmentResponse.Result?.Data is null)
                return NotFound();

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(assignmentResponse.Result.Data.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (rotationResponse.IsNotFound || rotationResponse.Result?.Data is null)
                return NotFound();

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                rotationResponse.Result.Data.GameType,
                AuthPolicies.ManageMapRotations,
                nameof(TerminateOrchestration),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var terminateResult = await syncApiClient.TerminateOrchestration(instanceId, cancellationToken).ConfigureAwait(false);

            if (terminateResult.Success)
            {
                this.AddAlertSuccess($"Orchestration '{instanceId}' terminated. You can now re-trigger the operation.");
            }
            else
            {
                this.AddAlertDanger($"Failed to terminate orchestration '{instanceId}': {terminateResult.Error}");
            }

            return RedirectToAction(nameof(AssignmentStatus), new { id = assignmentId });
        }, nameof(TerminateOrchestration)).ConfigureAwait(false);
    }

    [HttpGet]
    public IActionResult Import()
    {
        return View(new ImportMapRotationsViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(ImportMapRotationsViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!supportedGameTypes.Contains(model.GameType))
            {
                ModelState.AddModelError(nameof(model.GameType), "Only Call of Duty 4 and Call of Duty 5 are supported.");
                return View(model);
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                model.GameType,
                AuthPolicies.CreateMapRotation,
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
                AuthPolicies.CreateMapRotation,
                nameof(ImportConfirm),
                "MapRotation").ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var selectedIndices = new HashSet<int>(model.SelectedIndices);
            var selectedRotations = draft.Rotations
                .Select((r, i) => (Rotation: r, Index: i))
                .Where(x => selectedIndices.Contains(x.Index))
                .Select(x => x.Rotation)
                .ToList();

            if (selectedRotations.Count == 0)
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
            var allMapNames = selectedRotations.SelectMany(r => r.MapNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

            foreach (var rotation in selectedRotations)
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

                    var createDto = new CreateMapRotationDto(draft.GameType, rotation.Title, rotation.GameMode)
                    {
                        Description = rotation.RawComment,
                        MapIds = mapIds
                    };

                    var createResult = await repositoryApiClient.MapRotations.V1
                        .CreateMapRotation(createDto, cancellationToken).ConfigureAwait(false);

                    if (createResult.IsSuccess && createResult.Result?.Data != null)
                    {
                        results.Add(new ImportResultItem
                        {
                            Title = rotation.Title,
                            Status = "Imported",
                            MapRotationId = createResult.Result.Data.MapRotationId
                        });
                    }
                    else
                    {
                        results.Add(new ImportResultItem
                        {
                            Title = rotation.Title,
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

            return View("ImportResult", new ImportMapRotationsResultViewModel
            {
                GameType = draft.GameType,
                ImportedCount = results.Count(r => r.Status == "Imported"),
                SkippedCount = selectedRotations.Count - results.Count,
                FailedCount = results.Count(r => r.Status == "Failed"),
                MapsCreatedCount = mapsCreated,
                Results = results
            });
        }, nameof(ImportConfirm)).ConfigureAwait(false);
    }
}
