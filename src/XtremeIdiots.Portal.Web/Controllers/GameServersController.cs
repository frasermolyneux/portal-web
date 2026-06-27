using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages game server administration and configuration
/// </summary>
/// <remarks>
/// Initializes a new instance of the GameServersController
/// </remarks>
/// <param name="authorizationService">Authorization service for policy-based access control</param>
/// <param name="repositoryApiClient">Repository API client for data access</param>
/// <param name="telemetryClient">Application Insights telemetry client</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize(Policy = AuthPolicies.GameServers_Read)]
public class GameServersController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IGameServerSettingsService gameServerSettingsService,
    TelemetryClient telemetryClient,
    ILogger<GameServersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Displays a list of game servers accessible to the current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with list of game servers</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, AdditionalPermission.GameServers_Read];
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, gameServerIds, null, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve game servers for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController).Replace("Controller", ""), new { id = 500 });
            }

            var gameServerCount = gameServersApiResponse.Result.Data.Items.Count();
            Logger.LogInformation("User {UserId} successfully accessed {GameServerCount} game servers",
                User.XtremeIdiotsId(), gameServerCount);

            return View(gameServersApiResponse.Result.Data.Items);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the drag-drop reorder page for game servers (SeniorAdmin only)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with all game servers in a reorderable list</returns>
    [HttpGet]
    public async Task<IActionResult> Reorder(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                null!,
                AuthPolicies.GameServers_Delete,
                nameof(Reorder),
                "GameServer",
                "Reorder").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, null, 0, 200, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve game servers for reorder by user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController).Replace("Controller", ""), new { id = 500 });
            }

            return View(gameServersApiResponse.Result.Data.Items);
        }, nameof(Reorder)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the create game server form
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with create game server form</returns>
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var canCreate = await authorizationService.AuthorizeAsync(User, PotentialAccessProbe.Instance, AuthPolicies.GameServers_Write).ConfigureAwait(false);
            if (!canCreate.Succeeded)
                return Forbid();

            AddGameTypeViewData();
            return await Task.FromResult(View(new GameServerViewModel())).ConfigureAwait(false);
        }, nameof(Create)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GameServerViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var modelValidationResult = CheckModelState(model, m => AddGameTypeViewData(m.GameType));
            if (modelValidationResult is not null)
                return modelValidationResult;

#pragma warning disable CS8604
            var createGameServerDto = new CreateGameServerDto(model.Title, model.GameType, model.Hostname, model.QueryPort);
#pragma warning restore CS8604

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                createGameServerDto.GameType,
                AuthPolicies.GameServers_Write,
                nameof(Create),
                "GameServer",
                $"GameType:{createGameServerDto.GameType}").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            createGameServerDto.Title = model.Title;
            createGameServerDto.Hostname = model.Hostname;
            createGameServerDto.QueryPort = model.QueryPort;

            createGameServerDto.AgentEnabled = model.AgentEnabled;
            createGameServerDto.SetFileTransportProperties(model.FileTransportEnabled, model.FileTransportType);
            createGameServerDto.RconEnabled = model.RconEnabled;
            createGameServerDto.BanFileSyncEnabled = model.BanFileSyncEnabled;
            createGameServerDto.BanFileRootPath = string.IsNullOrWhiteSpace(model.BanFileRootPath) ? "/" : model.BanFileRootPath;
            createGameServerDto.ServerListEnabled = model.ServerListEnabled;

            var createResult = await repositoryApiClient.GameServers.V1.CreateGameServer(createGameServerDto, cancellationToken).ConfigureAwait(false);

            if (createResult.IsSuccess)
            {
                TrackSuccessTelemetry("GameServerCreated", nameof(Create), new Dictionary<string, string>
                {
                    { nameof(GameType), createGameServerDto.GameType.ToString() },
                    { nameof(CreateGameServerDto.Title), createGameServerDto.Title ?? "Unknown" }
                });

                this.AddAlertSuccess($"The game server has been successfully created for {model.GameType}");
                return RedirectToAction(nameof(Index));
            }
            else
            {
                Logger.LogWarning("Failed to create game server for user {UserId} and game type {GameType}",
                    User.XtremeIdiotsId(), model.GameType);

                this.AddAlertDanger("Failed to create the game server. Please try again.");
                AddGameTypeViewData(model.GameType);
                return View(model);
            }
        }, "CreatePost").ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogWarning("Game server {GameServerId} not found when viewing details", id);
                return NotFound();
            }

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server data is null for {GameServerId}", id);
                return BadRequest();
            }

            var gameServerData = gameServerApiResponse.Result.Data;
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.GameServers_Read,
                nameof(Details),
                "GameServer",
                $"GameType:{gameServerData.GameType},GameServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_BanFileMonitors_Read];
            var (gameTypes, banFileMonitorIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            gameServerData.ClearNoPermissionBanFileMonitors(gameTypes, banFileMonitorIds);

            var fileTransportEnabled = gameServerData.FileTransportEnabled;
            var fileTransportType = gameServerData.FileTransportType;

            // Fetch configuration namespaces so the view reads from config API instead of legacy DTO properties
            try
            {
                var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                    .GetConfigurations(gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        gameServerSettingsService.PopulateDetailsViewData(ViewData, fileTransportType, config, Logger);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch configurations for game server {GameServerId} details view", id);
            }

            return View(gameServerData);
        }, nameof(Details)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogWarning("Game server {GameServerId} not found when editing", id);
                return NotFound();
            }

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server data is null for {GameServerId}", id);
                return BadRequest();
            }

            var gameServerData = gameServerApiResponse.Result.Data;
            AddGameTypeViewData(gameServerData.GameType);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.GameServers_Write,
                nameof(Edit),
                "GameServer",
                $"GameType:{gameServerData.GameType},GameServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var canEditFileTransport = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_FileTransport_Write).ConfigureAwait(false);

            var canEditGameServerRcon = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_Rcon_Write).ConfigureAwait(false);

            var canConfigureScreenshots = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Admin_Screenshots_Configure).ConfigureAwait(false);

            var editModel = new GameServerEditViewModel
            {
                GameServer = gameServerData.ToViewModel(),
                CanEditFileTransport = canEditFileTransport.Succeeded,
                CanEditRcon = canEditGameServerRcon.Succeeded,
                CanConfigureScreenshots = canConfigureScreenshots.Succeeded
            };
            var (requiredTagOptions, isRequiredTagsCatalogAvailable) = await GetAvailableRequiredTagsAsync(cancellationToken).ConfigureAwait(false);
            editModel.ApplyAvailableRequiredTags(requiredTagOptions, isRequiredTagsCatalogAvailable);

            // Fetch configuration namespaces
            try
            {
                var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                    .GetConfigurations(gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        // Skip credential namespaces if user lacks permission
                        if ((config.Namespace == "ftp" || config.Namespace == "sftp") && !editModel.CanEditFileTransport)
                            continue;
                        if (config.Namespace == "rcon" && !editModel.CanEditRcon)
                            continue;

                        PopulateConfigFromNamespace(editModel, config);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch configurations for game server {GameServerId}", id);
            }

            // Fetch global defaults for moderation and events override placeholders
            try
            {
                var globalConfigsResult = await repositoryApiClient.GlobalConfigurations.V1
                    .GetConfigurations(cancellationToken).ConfigureAwait(false);

                if (globalConfigsResult.IsSuccess && globalConfigsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in globalConfigsResult.Result.Data.Items)
                    {
                        PopulateGlobalDefaults(editModel, config);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch global configurations for placeholder defaults");
            }

            return View(editModel);
        }, nameof(Edit)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(GameServerEditViewModel model, CancellationToken cancellationToken = default)
    {
        return model.GameServer is null
            ? BadRequest()
            : await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(model.GameServer.GameServerId, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogWarning("Game server {GameServerId} not found when updating", model.GameServer.GameServerId);
                return NotFound();
            }

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server data is null for {GameServerId}", model.GameServer.GameServerId);
                return BadRequest();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.GameServers_Write,
                nameof(Edit),
                "GameServer",
                $"GameType:{gameServerData.GameType},GameServerId:{model.GameServer.GameServerId}",
                gameServerData).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var editGameServerDto = new EditGameServerDto(gameServerData.GameServerId)
            {
                Title = model.GameServer.Title,
                Hostname = model.GameServer.Hostname,
                QueryPort = model.GameServer.QueryPort
            };

            var canEditFileTransport = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_FileTransport_Write).ConfigureAwait(false);
            var canEditGameServerRcon = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_Rcon_Write).ConfigureAwait(false);
            var canConfigureScreenshots = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Admin_Screenshots_Configure).ConfigureAwait(false);
            var (requiredTagOptions, isRequiredTagsCatalogAvailable) = await GetAvailableRequiredTagsAsync(cancellationToken).ConfigureAwait(false);
            model.ApplyAvailableRequiredTags(requiredTagOptions, isRequiredTagsCatalogAvailable);

            if (!canConfigureScreenshots.Succeeded)
            {
                model.ScreenshotConfigEnabled = false;
                model.ScreenshotConfigDirectoryPath = null;
                model.ScreenshotConfigFilePattern = GameServerEditViewModel.DefaultScreenshotFilePattern;
                model.ScreenshotConfigPollIntervalSeconds = GameServerEditViewModel.DefaultScreenshotPollIntervalSeconds;

                ModelState.Remove(nameof(GameServerEditViewModel.ScreenshotConfigEnabled));
                ModelState.Remove(nameof(GameServerEditViewModel.ScreenshotConfigDirectoryPath));
                ModelState.Remove(nameof(GameServerEditViewModel.ScreenshotConfigFilePattern));
                ModelState.Remove(nameof(GameServerEditViewModel.ScreenshotConfigPollIntervalSeconds));
            }

            var modelValidationResult = CheckModelState(model, m => AddGameTypeViewData(m.GameServer.GameType));
            if (modelValidationResult is not null)
            {
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return modelValidationResult;
            }

            // Preserve existing passwords when the user leaves password fields blank
            var passwordsPreserved = await PreserveExistingPasswordsAsync(model, gameServerData.GameServerId, canEditFileTransport.Succeeded, canEditGameServerRcon.Succeeded, cancellationToken).ConfigureAwait(false);
            if (!passwordsPreserved)
            {
                ModelState.AddModelError(string.Empty, "Failed to verify existing passwords. Please try again.");
                AddGameTypeViewData(model.GameServer.GameType);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            editGameServerDto.AgentEnabled = model.GameServer.AgentEnabled;

            var existingFileTransportEnabled = gameServerData.FileTransportEnabled;
            var existingFileTransportType = gameServerData.FileTransportType;

            var selectedFileTransportEnabled = canEditFileTransport.Succeeded
                ? model.GameServer.FileTransportEnabled
                : existingFileTransportEnabled;

            var selectedFileTransportType = canEditFileTransport.Succeeded
                ? model.GameServer.FileTransportType
                : existingFileTransportType;

            if (canEditFileTransport.Succeeded
                && selectedFileTransportEnabled
                && selectedFileTransportType == FileTransportType.Sftp
                && string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint))
            {
                ModelState.AddModelError(nameof(model.FtpConfigHostKeyFingerprint), "SFTP host key fingerprint is required when SFTP is enabled.");
                AddGameTypeViewData(model.GameServer.GameType);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            if (canEditFileTransport.Succeeded
                && !IsValidMapsRootPath(model.FileTransportConfigMapsRootPath))
            {
                ModelState.AddModelError(nameof(model.FtpConfigMapsRootPath), "Maps root path cannot contain path traversal segments.");
                AddGameTypeViewData(model.GameServer.GameType);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            editGameServerDto.SetFileTransportProperties(selectedFileTransportEnabled, selectedFileTransportType);
            editGameServerDto.RconEnabled = model.GameServer.RconEnabled;
            editGameServerDto.BanFileSyncEnabled = model.GameServer.BanFileSyncEnabled;
            editGameServerDto.BanFileRootPath = string.IsNullOrWhiteSpace(model.GameServer.BanFileRootPath) ? "/" : model.GameServer.BanFileRootPath;
            editGameServerDto.ServerListEnabled = model.GameServer.ServerListEnabled;

            var updateResult = await repositoryApiClient.GameServers.V1.UpdateGameServer(editGameServerDto, cancellationToken).ConfigureAwait(false);

            if (!updateResult.IsSuccess)
            {
                Logger.LogWarning("Failed to update game server {GameServerId} for user {UserId}",
                    model.GameServer.GameServerId, User.XtremeIdiotsId());

                this.AddAlertDanger("Failed to update the game server. Please try again.");
                AddGameTypeViewData(model.GameServer.GameType);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            // Save configuration namespaces
            var configErrors = new List<string>();
            await SaveConfigNamespacesAsync(model, gameServerData.GameServerId, canEditFileTransport.Succeeded, canEditGameServerRcon.Succeeded, canConfigureScreenshots.Succeeded, configErrors, cancellationToken).ConfigureAwait(false);

            // Track toggle changes for audit trail
            var serverTitle = model.GameServer.Title ?? "";
            TrackToggleChange(gameServerData.GameServerId, serverTitle, nameof(GameServerDto.AgentEnabled), gameServerData.AgentEnabled, model.GameServer.AgentEnabled);
            TrackToggleChange(gameServerData.GameServerId, serverTitle, "FileTransportEnabled", existingFileTransportEnabled, selectedFileTransportEnabled);
            TrackToggleChange(gameServerData.GameServerId, serverTitle, nameof(GameServerDto.RconEnabled), gameServerData.RconEnabled, model.GameServer.RconEnabled);
            TrackToggleChange(gameServerData.GameServerId, serverTitle, nameof(GameServerDto.BanFileSyncEnabled), gameServerData.BanFileSyncEnabled, model.GameServer.BanFileSyncEnabled);
            TrackToggleChange(gameServerData.GameServerId, serverTitle, nameof(GameServerDto.ServerListEnabled), gameServerData.ServerListEnabled, model.GameServer.ServerListEnabled);

            TrackSuccessTelemetry("GameServerUpdated", nameof(Edit), new Dictionary<string, string>
            {
                { nameof(GameServerDto.GameServerId), gameServerData.GameServerId.ToString() },
                { nameof(GameType), gameServerData.GameType.ToString() },
                { nameof(GameServerDto.Title), gameServerData.Title ?? "Unknown" }
            });

            if (configErrors.Count > 0)
            {
                this.AddAlertWarning($"The game server {gameServerData.Title} has been updated but some configuration sections failed to save: {string.Join(", ", configErrors)}");
            }
            else
            {
                this.AddAlertSuccess($"The game server {gameServerData.Title} has been updated for {gameServerData.GameType}");
            }

            return RedirectToAction(nameof(Index));
        }, "EditPost").ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogWarning("Game server {GameServerId} not found when deleting", id);
                return NotFound();
            }

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server data is null for {GameServerId}", id);
                return BadRequest();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var canDeleteGameServer = await authorizationService.AuthorizeAsync(User, AuthPolicies.GameServers_Delete).ConfigureAwait(false);
            if (!canDeleteGameServer.Succeeded)
            {
                TrackUnauthorizedAccessAttempt(nameof(Delete), "GameServer", $"GameType:{gameServerData.GameType},GameServerId:{id}", gameServerData);
                return Unauthorized();
            }

            return View(gameServerData.ToViewModel());
        }, nameof(Delete)).ConfigureAwait(false);
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogWarning("Game server {GameServerId} not found when confirming deletion", id);
                return NotFound();
            }

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server data is null for {GameServerId}", id);
                return BadRequest();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var canDeleteGameServer = await authorizationService.AuthorizeAsync(User, AuthPolicies.GameServers_Delete).ConfigureAwait(false);
            if (!canDeleteGameServer.Succeeded)
            {
                TrackUnauthorizedAccessAttempt(nameof(Delete), "GameServer", $"GameType:{gameServerData.GameType},GameServerId:{id}", gameServerData);
                return Unauthorized();
            }

            var deleteResult = await repositoryApiClient.GameServers.V1.DeleteGameServer(id, cancellationToken).ConfigureAwait(false);

            if (deleteResult.IsSuccess)
            {
                TrackSuccessTelemetry("GameServerDeleted", nameof(Delete), new Dictionary<string, string>
                {
                    { nameof(GameServerDto.GameServerId), gameServerData.GameServerId.ToString() },
                    { nameof(GameType), gameServerData.GameType.ToString() },
                    { nameof(GameServerDto.Title), gameServerData.Title ?? "Unknown" }
                });

                this.AddAlertSuccess($"The game server {gameServerData.Title} has been deleted for {gameServerData.GameType}");
                return RedirectToAction(nameof(Index));
            }
            else
            {
                Logger.LogWarning("Failed to delete game server {GameServerId} for user {UserId}",
                    id, User.XtremeIdiotsId());

                this.AddAlertDanger("Failed to delete the game server. Please try again.");
                return RedirectToAction(nameof(Index));
            }
        }, nameof(DeleteConfirmed)).ConfigureAwait(false);
    }

    private void AddGameTypeViewData(GameType? selected = null)
    {
        try
        {
            selected ??= GameType.Unknown;

            var gameTypes = User.GetGameTypesForGameServers();
            ViewData[nameof(GameType)] = new SelectList(gameTypes, selected);

            Logger.LogDebug("Added {GameTypeCount} game types to ViewData with {SelectedGameType} selected",
                gameTypes.Count, selected);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding game type ViewData for user {UserId}", User.XtremeIdiotsId());

            ViewData[nameof(GameType)] = new SelectList(Array.Empty<GameType>(), selected ?? GameType.Unknown);
        }
    }

    private void PopulateConfigFromNamespace(GameServerEditViewModel editModel, ConfigurationDto config)
    {
        gameServerSettingsService.PopulateConfigFromNamespace(editModel, config, Logger);
    }

    private static bool IsValidMapsRootPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(segment => segment == "..");
    }

    private void PopulateGlobalDefaults(GameServerEditViewModel editModel, ConfigurationDto config)
    {
        gameServerSettingsService.PopulateGlobalDefaults(editModel, config, Logger);
    }

    private async Task RepopulateAuthFlags(GameServerEditViewModel model, GameType gameType)
    {
        var canEditFileTransport = await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.GameServers_Credentials_FileTransport_Write).ConfigureAwait(false);
        var canEditRcon = await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.GameServers_Credentials_Rcon_Write).ConfigureAwait(false);
        var canConfigureScreenshots = await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.GameServers_Admin_Screenshots_Configure).ConfigureAwait(false);
        model.CanEditFileTransport = canEditFileTransport.Succeeded;
        model.CanEditRcon = canEditRcon.Succeeded;
        model.CanConfigureScreenshots = canConfigureScreenshots.Succeeded;
    }

    private async Task<bool> PreserveExistingPasswordsAsync(
        GameServerEditViewModel model,
        Guid gameServerId,
        bool canEditFileTransport,
        bool canEditRcon,
        CancellationToken cancellationToken)
    {
        var activeTransportNamespace = GameServerEditViewModel.GetFileTransportNamespace(model.GameServer.FileTransportType);
        var needsFileTransportPassword = canEditFileTransport && string.IsNullOrEmpty(model.FileTransportConfigPassword);
        var needsFileTransportHostKeyFingerprint = canEditFileTransport
            && string.Equals(activeTransportNamespace, "sftp", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint);
        var needsRconPassword = canEditRcon && string.IsNullOrEmpty(model.RconConfigPassword);

        if (!needsFileTransportPassword && !needsFileTransportHostKeyFingerprint && !needsRconPassword)
            return true;

        try
        {
            var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                .GetConfigurations(gameServerId, cancellationToken).ConfigureAwait(false);

            if (!configsResult.IsSuccess || configsResult.Result?.Data?.Items == null)
                return false;

            foreach (var config in configsResult.Result.Data.Items)
            {
                gameServerSettingsService.PopulateExistingCredentials(
                    model,
                    activeTransportNamespace,
                    config,
                    needsFileTransportPassword,
                    needsFileTransportHostKeyFingerprint,
                    needsRconPassword,
                    Logger);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch existing configurations for password preservation on game server {GameServerId}", gameServerId);
            return false;
        }
    }

    private async Task SaveConfigNamespacesAsync(
        GameServerEditViewModel model,
        Guid gameServerId,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var serverTitle = model.GameServer.Title ?? "";
        var namespacesToUpsert = gameServerSettingsService.BuildNamespaceConfigurations(model, canEditFileTransport, canEditRcon, canConfigureScreenshots);
        var upsertedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ns, json) in namespacesToUpsert)
        {
            upsertedNamespaces.Add(ns);
            await UpsertConfigSafeAsync(gameServerId, ns, json, serverTitle, errors, cancellationToken).ConfigureAwait(false);
        }

        foreach (var ns in gameServerSettingsService.DeletedNamespaces)
        {
            if (upsertedNamespaces.Contains(ns))
            {
                continue;
            }

            await DeleteConfigSafeAsync(gameServerId, ns, errors, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertConfigSafeAsync(
        Guid gameServerId,
        string ns,
        string configJson,
        string serverTitle,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryApiClient.GameServerConfigurations.V1.UpsertConfiguration(
                gameServerId, ns, new UpsertConfigurationDto { Configuration = configJson }, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("Failed to upsert configuration namespace '{Namespace}' for game server {GameServerId}", ns, gameServerId);
                errors.Add(ns);
            }
            else
            {
                TrackSuccessTelemetry("GameServerConfigChanged", "EditConfig", new Dictionary<string, string>
                {
                    { "GameServerId", gameServerId.ToString() },
                    { "ServerTitle", serverTitle },
                    { "Namespace", ns }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error upserting configuration namespace '{Namespace}' for game server {GameServerId}", ns, gameServerId);
            errors.Add(ns);
        }
    }

    private async Task DeleteConfigSafeAsync(
        Guid gameServerId,
        string ns,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryApiClient.GameServerConfigurations.V1
                .DeleteConfiguration(gameServerId, ns, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess && !result.IsNotFound)
            {
                Logger.LogWarning("Failed to delete configuration namespace '{Namespace}' for game server {GameServerId}", ns, gameServerId);
                errors.Add(ns);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error deleting configuration namespace '{Namespace}' for game server {GameServerId}", ns, gameServerId);
            errors.Add(ns);
        }
    }

    private void TrackToggleChange(Guid gameServerId, string serverTitle, string toggleName, bool oldValue, bool newValue)
    {
        if (oldValue != newValue)
        {
            TrackSuccessTelemetry("GameServerToggleChanged", "EditToggle", new Dictionary<string, string>
            {
                { "GameServerId", gameServerId.ToString() },
                { "ServerTitle", serverTitle },
                { "Toggle", toggleName },
                { "OldValue", oldValue.ToString() },
                { "NewValue", newValue.ToString() }
            });
        }
    }

    private async Task<(IReadOnlyList<RequiredTagOptionViewModel> Tags, bool IsCatalogAvailable)> GetAvailableRequiredTagsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var tags = new List<RequiredTagOptionViewModel>();
        var skip = 0;

        while (true)
        {
            var tagsResponse = await repositoryApiClient.Tags.V1.GetTags(skip, pageSize, cancellationToken).ConfigureAwait(false);
            if (!tagsResponse.IsSuccess || tagsResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve tags for game server required tags list");
                return ([], false);
            }

            var page = tagsResponse.Result.Data.Items.Where(static tag => tag is not null).ToList();
            if (page.Count == 0)
            {
                break;
            }

            tags.AddRange(page
                .Where(static tag => !string.IsNullOrWhiteSpace(tag.Name))
                .Select(static tag => new RequiredTagOptionViewModel
                {
                    Name = tag.Name,
                    DisplayName = tag.Name
                }));

            var pagination = tagsResponse.Result.Pagination;
            if (pagination is not null)
            {
                var totalAvailable = Math.Max(pagination.TotalCount, pagination.FilteredCount);
                var nextSkip = pagination.Skip + pagination.Top;
                if (nextSkip <= skip || nextSkip >= totalAvailable)
                {
                    break;
                }

                skip = nextSkip;
                continue;
            }

            if (page.Count < pageSize)
            {
                break;
            }

            skip += page.Count;
        }

        return ([.. tags
            .GroupBy(static tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static tag => tag.DisplayName, StringComparer.OrdinalIgnoreCase)], true);
    }
}