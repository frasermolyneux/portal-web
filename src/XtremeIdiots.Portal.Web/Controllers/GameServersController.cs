using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
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
[Authorize(Policy = AuthPolicies.AccessGameServers)]
public class GameServersController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<GameServersController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
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
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameServer];
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
    /// Displays the create game server form
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with create game server form</returns>
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
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
                AuthPolicies.CreateGameServer,
                nameof(Create),
                "GameServer",
                $"GameType:{createGameServerDto.GameType}").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            createGameServerDto.Title = model.Title;
            createGameServerDto.Hostname = model.Hostname;
            createGameServerDto.QueryPort = model.QueryPort;
            createGameServerDto.ServerListPosition = model.ServerListPosition;

            createGameServerDto.AgentEnabled = model.AgentEnabled;
            createGameServerDto.FtpEnabled = model.FtpEnabled;
            createGameServerDto.RconEnabled = model.RconEnabled;
            createGameServerDto.BanFileSyncEnabled = model.BanFileSyncEnabled;
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
                AuthPolicies.ViewGameServer,
                nameof(Details),
                "GameServer",
                $"GameType:{gameServerData.GameType},GameServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, UserProfileClaimType.BanFileMonitor];
            var (gameTypes, banFileMonitorIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            gameServerData.ClearNoPermissionBanFileMonitors(gameTypes, banFileMonitorIds);

            // Fetch configuration namespaces so the view reads from config API instead of legacy DTO properties
            try
            {
                var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                    .GetConfigurations(gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        if (string.IsNullOrWhiteSpace(config.Configuration))
                            continue;

                        using var doc = JsonDocument.Parse(config.Configuration);
                        var root = doc.RootElement;

                        switch (config.Namespace)
                        {
                            case "ftp":
                                ViewBag.FtpHostname = GetStringProperty(root, "hostname");
                                ViewBag.FtpPort = GetIntProperty(root, "port", 21);
                                ViewBag.FtpUsername = GetStringProperty(root, "username");
                                ViewBag.FtpPassword = GetStringProperty(root, "password");
                                break;
                            case "rcon":
                                ViewBag.RconPassword = GetStringProperty(root, "password");
                                break;
                            case "serverlist":
                                break;
                            default:
                                break;
                        }
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
                AuthPolicies.EditGameServer,
                nameof(Edit),
                "GameServer",
                $"GameType:{gameServerData.GameType},GameServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var canEditGameServerFtp = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.EditGameServerFtp).ConfigureAwait(false);

            var canEditGameServerRcon = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.EditGameServerRcon).ConfigureAwait(false);

            var editModel = new GameServerEditViewModel
            {
                GameServer = gameServerData.ToViewModel(),
                CanEditFtp = canEditGameServerFtp.Succeeded,
                CanEditRcon = canEditGameServerRcon.Succeeded
            };

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
                        if (config.Namespace == "ftp" && !editModel.CanEditFtp)
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

            return View(editModel);
        }, nameof(Edit)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(GameServerEditViewModel model, CancellationToken cancellationToken = default)
    {
        if (model.GameServer is null)
            return BadRequest();

        return await ExecuteWithErrorHandlingAsync(async () =>
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

            var modelValidationResult = CheckModelState(model, m => AddGameTypeViewData(m.GameServer.GameType));
            if (modelValidationResult is not null)
            {
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return modelValidationResult;
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.EditGameServer,
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
                QueryPort = model.GameServer.QueryPort,
                ServerListPosition = model.GameServer.ServerListPosition
            };

            var canEditGameServerFtp = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.EditGameServerFtp).ConfigureAwait(false);
            var canEditGameServerRcon = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.EditGameServerRcon).ConfigureAwait(false);

            // Preserve existing passwords when the user leaves password fields blank
            var passwordsPreserved = await PreserveExistingPasswordsAsync(model, gameServerData.GameServerId, canEditGameServerFtp.Succeeded, canEditGameServerRcon.Succeeded, cancellationToken).ConfigureAwait(false);
            if (!passwordsPreserved)
            {
                ModelState.AddModelError(string.Empty, "Failed to verify existing passwords. Please try again.");
                AddGameTypeViewData(model.GameServer.GameType);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            editGameServerDto.AgentEnabled = model.GameServer.AgentEnabled;
            editGameServerDto.FtpEnabled = model.GameServer.FtpEnabled;
            editGameServerDto.RconEnabled = model.GameServer.RconEnabled;
            editGameServerDto.BanFileSyncEnabled = model.GameServer.BanFileSyncEnabled;
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
            await SaveConfigNamespacesAsync(model, gameServerData.GameServerId, canEditGameServerFtp.Succeeded, canEditGameServerRcon.Succeeded, configErrors, cancellationToken).ConfigureAwait(false);

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

            var canDeleteGameServer = await authorizationService.AuthorizeAsync(User, AuthPolicies.DeleteGameServer).ConfigureAwait(false);
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

            var canDeleteGameServer = await authorizationService.AuthorizeAsync(User, AuthPolicies.DeleteGameServer).ConfigureAwait(false);
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

    private readonly static JsonSerializerOptions configJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void PopulateConfigFromNamespace(GameServerEditViewModel editModel, ConfigurationDto config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
                return;

            using var doc = JsonDocument.Parse(config.Configuration);
            var root = doc.RootElement;

            switch (config.Namespace)
            {
                case "ftp":
                    editModel.FtpConfigHostname = GetStringProperty(root, "hostname");
                    editModel.FtpConfigPort = GetIntProperty(root, "port", 21);
                    editModel.FtpConfigUsername = GetStringProperty(root, "username");
                    editModel.FtpConfigPassword = GetStringProperty(root, "password");
                    break;
                case "rcon":
                    editModel.RconConfigPassword = GetStringProperty(root, "password");
                    break;
                case "agent":
                    editModel.AgentConfigLogFilePath = GetStringProperty(root, "logFilePath");
                    editModel.AgentConfigRconSyncEnabled = GetBoolProperty(root, "rconSyncEnabled", true);
                    break;
                case "banfiles":
                    editModel.BanFileSyncConfigCheckIntervalSeconds = GetIntProperty(root, "checkIntervalSeconds", 60);
                    break;
                case "serverlist":
                    editModel.ServerListConfigHtmlBanner = GetStringProperty(root, "htmlBanner");
                    break;
                default:
                    Logger.LogDebug("Unknown configuration namespace '{Namespace}' for game server", config.Namespace);
                    break;
            }
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private static bool GetBoolProperty(JsonElement root, string propertyName, bool defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Undefined => defaultValue,
                JsonValueKind.Object => defaultValue,
                JsonValueKind.Array => defaultValue,
                JsonValueKind.String => defaultValue,
                JsonValueKind.Number => defaultValue,
                JsonValueKind.Null => defaultValue,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    private async Task RepopulateAuthFlags(GameServerEditViewModel model, GameType gameType)
    {
        var canEditFtp = await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.EditGameServerFtp).ConfigureAwait(false);
        var canEditRcon = await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.EditGameServerRcon).ConfigureAwait(false);
        model.CanEditFtp = canEditFtp.Succeeded;
        model.CanEditRcon = canEditRcon.Succeeded;
    }

    private async Task<bool> PreserveExistingPasswordsAsync(
        GameServerEditViewModel model,
        Guid gameServerId,
        bool canEditFtp,
        bool canEditRcon,
        CancellationToken cancellationToken)
    {
        var needsFtpPassword = canEditFtp && string.IsNullOrEmpty(model.FtpConfigPassword);
        var needsRconPassword = canEditRcon && string.IsNullOrEmpty(model.RconConfigPassword);

        if (!needsFtpPassword && !needsRconPassword)
            return true;

        try
        {
            var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                .GetConfigurations(gameServerId, cancellationToken).ConfigureAwait(false);

            if (!configsResult.IsSuccess || configsResult.Result?.Data?.Items == null)
                return false;

            foreach (var config in configsResult.Result.Data.Items)
            {
                if (string.IsNullOrWhiteSpace(config.Configuration))
                    continue;

                if (needsFtpPassword && config.Namespace == "ftp")
                {
                    using var doc = JsonDocument.Parse(config.Configuration);
                    model.FtpConfigPassword = GetStringProperty(doc.RootElement, "password");
                }
                else if (needsRconPassword && config.Namespace == "rcon")
                {
                    using var doc = JsonDocument.Parse(config.Configuration);
                    model.RconConfigPassword = GetStringProperty(doc.RootElement, "password");
                }
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
        bool canEditFtp,
        bool canEditRcon,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        // Save FTP config
        if (canEditFtp)
        {
            await UpsertConfigSafeAsync(gameServerId, "ftp", JsonSerializer.Serialize(new
            {
                hostname = model.FtpConfigHostname,
                port = model.FtpConfigPort,
                username = model.FtpConfigUsername,
                password = model.FtpConfigPassword
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);
        }

        // Save RCON config
        if (canEditRcon)
        {
            await UpsertConfigSafeAsync(gameServerId, "rcon", JsonSerializer.Serialize(new
            {
                password = model.RconConfigPassword
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);
        }

        // Save Agent config
        if (model.GameServer.AgentEnabled)
        {
            await UpsertConfigSafeAsync(gameServerId, "agent", JsonSerializer.Serialize(new
            {
                logFilePath = model.AgentConfigLogFilePath,
                rconSyncEnabled = model.AgentConfigRconSyncEnabled
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);
        }

        // Save Ban File Sync config
        if (model.GameServer.BanFileSyncEnabled)
        {
            await UpsertConfigSafeAsync(gameServerId, "banfiles", JsonSerializer.Serialize(new
            {
                checkIntervalSeconds = model.BanFileSyncConfigCheckIntervalSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);
        }

        // Save Server List config
        if (model.GameServer.ServerListEnabled)
        {
            await UpsertConfigSafeAsync(gameServerId, "serverlist", JsonSerializer.Serialize(new
            {
                htmlBanner = model.ServerListConfigHtmlBanner
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertConfigSafeAsync(
        Guid gameServerId,
        string ns,
        string configJson,
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
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error upserting configuration namespace '{Namespace}' for game server {GameServerId}", ns, gameServerId);
            errors.Add(ns);
        }
    }
}