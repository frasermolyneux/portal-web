using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.AccessMapRotations)]
public class MapRotationsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapRotationsController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    private readonly static GameType[] supportedGameTypes = [GameType.CallOfDuty4, GameType.CallOfDuty5];

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

            var mapIds = new List<Guid>();
            if (!string.IsNullOrWhiteSpace(model.MapIdsText))
            {
                foreach (var part in model.MapIdsText.Split([',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var mapId))
                        mapIds.Add(mapId);
                    else
                        ModelState.AddModelError(nameof(model.MapIdsText), $"Invalid map ID: '{part.Trim()}'");
                }

                if (!ModelState.IsValid)
                    return View(model);
            }

            var createDto = new CreateMapRotationDto(model.GameType, model.Title, model.GameMode)
            {
                Description = model.Description,
                MapIds = mapIds
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

            return View(new EditMapRotationViewModel
            {
                MapRotationId = rotation.MapRotationId,
                Title = rotation.Title,
                Description = rotation.Description,
                GameMode = rotation.GameMode,
                GameType = rotation.GameType,
                Version = rotation.Version,
                MapIdsText = rotation.MapRotationMaps?.Count > 0
                    ? string.Join(", ", rotation.MapRotationMaps.Select(m => m.MapId))
                    : null
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

            var mapIds = new List<Guid>();
            if (!string.IsNullOrWhiteSpace(model.MapIdsText))
            {
                foreach (var part in model.MapIdsText.Split([',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var mapId))
                        mapIds.Add(mapId);
                    else
                        ModelState.AddModelError(nameof(model.MapIdsText), $"Invalid map ID: '{part.Trim()}'");
                }

                if (!ModelState.IsValid)
                    return View(model);
            }

            var updateDto = new UpdateMapRotationDto(model.MapRotationId)
            {
                Title = model.Title,
                Description = model.Description,
                GameMode = model.GameMode,
                MapIds = mapIds
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

            // Load game server details for each assignment
            if (rotation.ServerAssignments?.Count > 0)
            {
                foreach (var assignment in rotation.ServerAssignments)
                {
                    var serverResponse = await repositoryApiClient.GameServers.V1.GetGameServer(assignment.GameServerId, cancellationToken).ConfigureAwait(false);
                    if (serverResponse.IsSuccess && serverResponse.Result?.Data != null)
                    {
                        ViewData[$"Server_{assignment.GameServerId}"] = serverResponse.Result.Data;
                    }
                }
            }

            return View(new MapRotationDetailsViewModel
            {
                Rotation = rotation,
                Maps = maps
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
}
