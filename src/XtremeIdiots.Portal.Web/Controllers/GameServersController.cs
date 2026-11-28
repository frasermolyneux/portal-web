using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MX.Observability.ApplicationInsights.Auditing;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.FileTransport;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Moderation;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
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
    private readonly static JsonSerializerOptions cod4xPluginJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private bool skipCod4xPluginNamespaceMutations;

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
            string[] requiredClaims = [UserProfileClaimType.Webmaster, UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, AdditionalPermission.GameServers_Read];
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
            AddPlatformViewData();
            return await Task.FromResult(View(new GameServerViewModel())).ConfigureAwait(false);
        }, nameof(Create)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GameServerViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!Enum.IsDefined(model.Platform) || model.Platform == GameServerPlatform.Unknown)
            {
                ModelState.AddModelError(nameof(model.Platform), "Platform is required.");
            }

            var modelValidationResult = CheckModelState(model, m =>
            {
                AddGameTypeViewData(m.GameType);
                AddPlatformViewData(m.Platform);
            });
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
            createGameServerDto.Platform = model.Platform;

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
                AddPlatformViewData(model.Platform);
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

            string[] requiredClaims = [UserProfileClaimType.Webmaster, UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_BanFileMonitors_Read];
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
            AddPlatformViewData(gameServerData.Platform);

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
            await PopulateRuleCapabilitiesAsync(editModel, gameServerData.GameServerId, gameServerData.GameType, cancellationToken).ConfigureAwait(false);

            // Fetch configuration namespaces
            try
            {
                var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                    .GetConfigurations(gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);

                if (!configsResult.IsSuccess || configsResult.Result?.Data?.Items is null)
                {
                    Logger.LogWarning("Failed to retrieve server configurations for {GameServerId}", id);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        if (string.Equals(config.Namespace, VpnProtectionSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateVpnProtectionSettings(config, out var vpnErrors))
                        {
                            Logger.LogWarning("Invalid VPN Protection settings for server {GameServerId}: {Errors}", id, string.Join("; ", vpnErrors));
                            return UnprocessableEntity("Saved VPN Protection settings are unreadable or use an unsupported schema.");
                        }

                        if (string.Equals(config.Namespace, SiegeModeSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateSiegeModeSettings(config, out var siegeErrors))
                        {
                            Logger.LogWarning("Invalid Siege Mode settings for server {GameServerId}: {Errors}", id, string.Join("; ", siegeErrors));
                            return UnprocessableEntity("Saved Siege Mode settings are unreadable or use an unsupported schema.");
                        }

                        if (string.Equals(config.Namespace, WelcomeRulesSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateWelcomeRulesSettings(config, out var welcomeErrors))
                        {
                            Logger.LogWarning("Invalid Welcome Rules settings for server {GameServerId}: {Errors}", id, string.Join("; ", welcomeErrors));
                            return UnprocessableEntity("Saved Welcome Rules settings are unreadable or use an unsupported schema.");
                        }

                        // Skip credential namespaces if user lacks permission
                        if ((config.Namespace == "ftp" || config.Namespace == "sftp") && !editModel.CanEditFileTransport)
                            continue;
                        if (config.Namespace == "rcon" && !editModel.CanEditRcon)
                            continue;

                        if (string.Equals(config.Namespace, BroadcastSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
                            editModel.BroadcastsReadOnly |= BroadcastEditorCompatibility.RequiresReadOnly(config.Configuration);
                        if (string.Equals(config.Namespace, ChatCommandSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
                            editModel.ChatCommandsReadOnly = ChatCommandEditorCompatibility.RequiresReadOnly(config.Configuration, gameServerData.GameType);
                        if (string.Equals(config.Namespace, ModerationSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
                            editModel.ModerationReadOnly = ModerationEditorCompatibility.RequiresReadOnly(config.Configuration);

                        PopulateConfigFromNamespace(editModel, config);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch configurations for game server {GameServerId}", id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // Fetch global defaults for moderation and events override placeholders
            try
            {
                var globalConfigsResult = await repositoryApiClient.GlobalConfigurations.V1
                    .GetConfigurations(cancellationToken).ConfigureAwait(false);

                if (!globalConfigsResult.IsSuccess || globalConfigsResult.Result?.Data?.Items is null)
                {
                    Logger.LogWarning("Failed to retrieve global configurations for game server {GameServerId}", id);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (globalConfigsResult.IsSuccess && globalConfigsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in globalConfigsResult.Result.Data.Items)
                    {
                        if (string.Equals(config.Namespace, VpnProtectionSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateGlobalVpnProtectionSettings(config, out var vpnErrors))
                        {
                            Logger.LogWarning("Invalid global VPN Protection settings: {Errors}", string.Join("; ", vpnErrors));
                            return UnprocessableEntity("Saved global VPN Protection settings are unreadable or use an unsupported schema.");
                        }

                        if (string.Equals(config.Namespace, SiegeModeSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateGlobalSiegeModeSettings(config, out var siegeErrors))
                        {
                            Logger.LogWarning("Invalid global Siege Mode settings: {Errors}", string.Join("; ", siegeErrors));
                            return UnprocessableEntity("Saved global Siege Mode settings are unreadable or use an unsupported schema.");
                        }

                        if (string.Equals(config.Namespace, WelcomeRulesSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                            && !gameServerSettingsService.TryValidateGlobalWelcomeRulesSettings(config, out var welcomeErrors))
                        {
                            Logger.LogWarning("Invalid global Welcome Rules settings: {Errors}", string.Join("; ", welcomeErrors));
                            return UnprocessableEntity("Saved global Welcome Rules settings are unreadable or use an unsupported schema.");
                        }

                        PopulateGlobalDefaults(editModel, config);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch global configurations for placeholder defaults");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
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
            model.GameServer.GameType = gameServerData.GameType;
            ModelState.Remove($"{nameof(GameServerEditViewModel.GameServer)}.{nameof(GameServerViewModel.GameType)}");

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

            var savedConfigurations = await repositoryApiClient.GameServerConfigurations.V1
                .GetConfigurations(gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);
            if (!savedConfigurations.IsSuccess || savedConfigurations.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve server configurations for {GameServerId}", gameServerData.GameServerId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var savedBroadcasts = new GameServerEditViewModel();
            foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                string.Equals(config.Namespace, BroadcastSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
                PopulateConfigFromNamespace(savedBroadcasts, config);
            model.SavedBroadcastSettings = savedBroadcasts.SavedBroadcastSettings;

            model.BroadcastsReadOnly = !BroadcastEditorCompatibility.HasCompleteSubmission(
                ModelState, model.BroadcastMessagesCount, model.BroadcastMessages, server: true)
                || savedConfigurations.Result.Data.Items.Any(config =>
                string.Equals(config.Namespace, BroadcastSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase) &&
                BroadcastEditorCompatibility.RequiresReadOnly(config.Configuration));

            if (model.BroadcastsReadOnly)
            {
                BroadcastEditorCompatibility.ClearSubmittedModelState(ModelState);
                model.BroadcastsEnabled = savedBroadcasts.BroadcastsEnabled;
                model.BroadcastsIntervalSeconds = savedBroadcasts.BroadcastsIntervalSeconds;
                model.BroadcastsMessagesMode = savedBroadcasts.BroadcastsMessagesMode;
                model.BroadcastMessages = savedBroadcasts.BroadcastMessages;
            }

            model.ChatCommandsReadOnly = !ModelState.ContainsKey("ChatCommands.Commands[0].Name")
                || savedConfigurations.Result.Data.Items.Any(config =>
                string.Equals(config.Namespace, ChatCommandSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase) &&
                ChatCommandEditorCompatibility.RequiresReadOnly(config.Configuration, gameServerData.GameType));

            model.ModerationReadOnly = !ModerationEditorCompatibility.HasCompleteSubmission(ModelState, server: true)
                || savedConfigurations.Result.Data.Items.Any(config =>
                string.Equals(config.Namespace, ModerationSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase) &&
                ModerationEditorCompatibility.RequiresReadOnly(config.Configuration));

            if (model.ModerationReadOnly)
            {
                ModerationEditorCompatibility.ClearSubmittedModelState(ModelState);
                var savedModeration = new GameServerEditViewModel();
                foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                    string.Equals(config.Namespace, ModerationSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
                    PopulateConfigFromNamespace(savedModeration, config);
                ModerationEditorCompatibility.Restore(model, savedModeration);
            }

            if (model.ChatCommandsReadOnly)
            {
                ChatCommandEditorCompatibility.ClearSubmittedModelState(ModelState);
                model.ChatCommands = new ChatCommandServerSettingsViewModel();
                foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                    string.Equals(config.Namespace, ChatCommandSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
                    PopulateConfigFromNamespace(model, config);
            }

            foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                string.Equals(config.Namespace, VpnProtectionSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
            {
                if (!gameServerSettingsService.TryValidateVpnProtectionSettings(config, out var vpnErrors))
                {
                    Logger.LogWarning("Invalid VPN Protection settings for server {GameServerId}: {Errors}", gameServerData.GameServerId, string.Join("; ", vpnErrors));
                    return UnprocessableEntity("Saved VPN Protection settings are unreadable or use an unsupported schema.");
                }
            }

            foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                string.Equals(config.Namespace, SiegeModeSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
            {
                if (!gameServerSettingsService.TryValidateSiegeModeSettings(config, out var siegeErrors))
                {
                    Logger.LogWarning("Invalid Siege Mode settings for server {GameServerId}: {Errors}", gameServerData.GameServerId, string.Join("; ", siegeErrors));
                    return UnprocessableEntity("Saved Siege Mode settings are unreadable or use an unsupported schema.");
                }
            }

            foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                string.Equals(config.Namespace, WelcomeRulesSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
            {
                if (!gameServerSettingsService.TryValidateWelcomeRulesSettings(config, out var welcomeErrors))
                {
                    Logger.LogWarning("Invalid Welcome Rules settings for server {GameServerId}: {Errors}", gameServerData.GameServerId, string.Join("; ", welcomeErrors));
                    return UnprocessableEntity("Saved Welcome Rules settings are unreadable or use an unsupported schema.");
                }
            }

            if (!Enum.IsDefined(model.GameServer.Platform) || model.GameServer.Platform == GameServerPlatform.Unknown)
            {
                var platformFieldName = $"{nameof(GameServerEditViewModel.GameServer)}.{nameof(GameServerViewModel.Platform)}";
                ModelState.AddModelError(platformFieldName, "Platform is required.");
            }

            var editGameServerDto = new EditGameServerDto(gameServerData.GameServerId)
            {
                Title = model.GameServer.Title,
                Hostname = model.GameServer.Hostname,
                QueryPort = model.GameServer.QueryPort,
                Platform = model.GameServer.Platform
            };

            var canEditFileTransport = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_FileTransport_Write).ConfigureAwait(false);
            var canEditGameServerRcon = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Credentials_Rcon_Write).ConfigureAwait(false);
            var canConfigureScreenshots = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Admin_Screenshots_Configure).ConfigureAwait(false);
            if (canEditFileTransport.Succeeded
                && model.GameServer.FileTransportEnabled
                && model.GameServer.FileTransportType == FileTransportType.Sftp
                && model.SftpConfigAuthenticationType is null)
            {
                ModelState.AddModelError(
                    nameof(GameServerEditViewModel.SftpConfigAuthenticationType),
                    "The SFTP Authentication field is required.");
            }

            var (requiredTagOptions, isRequiredTagsCatalogAvailable) = await GetAvailableRequiredTagsAsync(cancellationToken).ConfigureAwait(false);
            model.ApplyAvailableRequiredTags(requiredTagOptions, isRequiredTagsCatalogAvailable);
            await PopulateRuleCapabilitiesAsync(model, gameServerData.GameServerId, gameServerData.GameType, cancellationToken).ConfigureAwait(false);
            var globalDefaultsLoaded = await PopulateGlobalDefaultsAsync(model, cancellationToken).ConfigureAwait(false);
            if (!globalDefaultsLoaded)
            {
                model.GlobalBroadcastsReadOnly = true;
                ModelState.AddModelError(string.Empty, "Global settings could not be loaded. Retry before saving.");
            }

            if (!ModelState.ContainsKey("VpnProtection.Enabled")
                || !ModelState.ContainsKey("VpnProtection.InheritGlobalRules")
                || !ModelState.ContainsKey("VpnProtection.LocalRulesJson")
                || !ModelState.ContainsKey("VpnProtection.RuleOverridesJson"))
            {
                ModelState.AddModelError(nameof(GameServerEditViewModel.VpnProtection), "The VPN Protection settings section is missing or incomplete.");
            }

            if (!ModelState.ContainsKey("SiegeMode.Enabled")
                || !ModelState.ContainsKey("SiegeMode.InheritGlobalRules")
                || !ModelState.ContainsKey("SiegeMode.DurationMinutesOverride")
                || !ModelState.ContainsKey("SiegeMode.LocalRulesJson")
                || !ModelState.ContainsKey("SiegeMode.RuleOverridesJson"))
            {
                ModelState.AddModelError(nameof(GameServerEditViewModel.SiegeMode), "The Siege Mode settings section is missing or incomplete.");
            }

            if (!ModelState.ContainsKey("WelcomeRules.Enabled")
                || !ModelState.ContainsKey("WelcomeRules.InheritGlobalRules")
                || !ModelState.ContainsKey("WelcomeRules.ConnectionDelaySeconds")
                || !ModelState.ContainsKey("WelcomeRules.FreshnessSeconds")
                || !ModelState.ContainsKey("WelcomeRules.LocalRulesJson")
                || !ModelState.ContainsKey("WelcomeRules.RuleOverridesJson"))
            {
                ModelState.AddModelError(nameof(GameServerEditViewModel.WelcomeRules), "The Welcome Rules settings section is missing or incomplete.");
            }

            if (ModelState.IsValid)
            {
                var existing = new GameServerEditViewModel();
                foreach (var config in savedConfigurations.Result.Data.Items.Where(static config =>
                    string.Equals(config.Namespace, VpnProtectionSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(config.Namespace, SiegeModeSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(config.Namespace, WelcomeRulesSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)))
                    gameServerSettingsService.PopulateConfigFromNamespace(existing, config, Logger);
                RuleSettingsEnvelopeData.CopyFromAuthoritative(existing, model);

                var vpnErrors = RulesCapabilityResolver.FindNewUnsupported(
                    VpnProtectionSettingsJsonMapper.ToDocument(existing.VpnProtection),
                    VpnProtectionSettingsJsonMapper.ToDocument(model.VpnProtection), model.VpnRuleCompatibility);
                foreach (var error in vpnErrors)
                    ModelState.AddModelError(nameof(GameServerEditViewModel.VpnProtection), error);

                var siegeErrors = RulesCapabilityResolver.FindNewUnsupported(
                    SiegeModeServerSettingsMapper.ToDocument(existing.SiegeMode),
                    SiegeModeServerSettingsMapper.ToDocument(model.SiegeMode), model.SiegeRuleCompatibility);
                foreach (var error in siegeErrors)
                    ModelState.AddModelError(nameof(GameServerEditViewModel.SiegeMode), error);

                var welcomeErrors = RulesCapabilityResolver.FindNewUnsupported(
                    WelcomeRulesSettingsMapper.ToDocument(existing.WelcomeRules),
                    WelcomeRulesSettingsMapper.ToDocument(model.WelcomeRules), model.WelcomeRuleCompatibility);
                foreach (var error in welcomeErrors)
                    ModelState.AddModelError(nameof(GameServerEditViewModel.WelcomeRules), error);
            }

            if (ModelState.IsValid && globalDefaultsLoaded
                && RuleSettingsJson.TryDeserializeRules(model.WelcomeRules.LocalRulesJson, out _, out _)
                && RuleSettingsJson.TryDeserializeOverrides(model.WelcomeRules.RuleOverridesJson, out var welcomeOverrides, out _))
            {
                var global = WelcomeRulesSettingsMapper.ToDocument(model.GlobalWelcomeRules);
                var server = WelcomeRulesSettingsMapper.ToDocument(model.WelcomeRules);
                var knownIds = global.Rules.Select(static rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (server.InheritGlobalRules && welcomeOverrides.Any(item => !knownIds.Contains(item.RuleId)))
                    ModelState.AddModelError(nameof(GameServerEditViewModel.WelcomeRules),
                        "An override refers to a global Welcome Rules rule that is no longer available.");
                try
                {
                    var effective = FeatureRuleSetMerger.Merge(global, server);
                    var validation = new WelcomeRulesSettingsValidator().Validate(new WelcomeRulesSettingsDocument
                    {
                        Enabled = effective.Enabled,
                        Timing = effective.Timing,
                        Rules = [.. effective.Rules]
                    });
                    foreach (var error in validation.Errors)
                        ModelState.AddModelError(nameof(GameServerEditViewModel.WelcomeRules), error);
                }
                catch (ArgumentException ex)
                {
                    ModelState.AddModelError(nameof(GameServerEditViewModel.WelcomeRules), ex.Message);
                }
            }

            if (ModelState.IsValid
                && RuleSettingsJson.TryDeserializeRules(model.VpnProtection.LocalRulesJson, out _, out _)
                && RuleSettingsJson.TryDeserializeOverrides(model.VpnProtection.RuleOverridesJson, out var overrides, out _))
            {
                if (!globalDefaultsLoaded && overrides.Count > 0)
                {
                    ModelState.AddModelError(
                        $"{nameof(GameServerEditViewModel.VpnProtection)}.{nameof(VpnProtectionServerSettingsViewModel.RuleOverridesJson)}",
                        "Global VPN Protection rules could not be loaded. Retry before saving overrides.");
                }
                else if (globalDefaultsLoaded)
                {
                    var global = VpnProtectionSettingsJsonMapper.ToDocument(model.GlobalVpnProtection);
                    var server = VpnProtectionSettingsJsonMapper.ToDocument(model.VpnProtection);
                    var knownIds = global.Rules.Select(static rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (server.InheritGlobalRules && overrides.Any(item => !knownIds.Contains(item.RuleId)))
                    {
                        ModelState.AddModelError(
                            $"{nameof(GameServerEditViewModel.VpnProtection)}.{nameof(VpnProtectionServerSettingsViewModel.RuleOverridesJson)}",
                            "An override refers to a global rule that is no longer available.");
                    }

                    var effective = FeatureRuleSetMerger.Merge(global, server);
                    var validation = new VpnProtectionSettingsValidator().Validate(new VpnProtectionSettingsDocument
                    {
                        Enabled = effective.Enabled,
                        Rules = [.. effective.Rules]
                    });
                    foreach (var error in validation.Errors)
                        ModelState.AddModelError(nameof(GameServerEditViewModel.VpnProtection), error);
                }
            }

            if (ModelState.IsValid
                && RuleSettingsJson.TryDeserializeRules(model.SiegeMode.LocalRulesJson, out _, out _)
                && RuleSettingsJson.TryDeserializeOverrides(model.SiegeMode.RuleOverridesJson, out var siegeOverrides, out _))
            {
                if (!globalDefaultsLoaded && siegeOverrides.Count > 0)
                {
                    ModelState.AddModelError(nameof(GameServerEditViewModel.SiegeMode),
                        "Global Siege Mode rules could not be loaded. Retry before saving overrides.");
                }
                else if (globalDefaultsLoaded)
                {
                    var global = SiegeModeSettingsViewModelMapper.ToDocument(model.GlobalSiegeMode);
                    var server = SiegeModeServerSettingsMapper.ToDocument(model.SiegeMode);
                    var knownIds = global.Rules.Select(static rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (server.InheritGlobalRules && siegeOverrides.Any(item => !knownIds.Contains(item.RuleId)))
                        ModelState.AddModelError(nameof(GameServerEditViewModel.SiegeMode),
                            "An override refers to a global Siege Mode rule that is no longer available.");
                    var effective = SiegeModeSettingsMerger.Merge(global, server);
                    var validation = new SiegeModeSettingsValidator().Validate(SiegeModeSettingsMerger.Freeze(effective));
                    foreach (var error in validation.Errors)
                        ModelState.AddModelError(nameof(GameServerEditViewModel.SiegeMode), error);
                }
            }

            if (gameServerData.GameType == GameType.CallOfDuty4x)
            {
                var cod4xStatePreserved = await PreserveServerManagedCod4xLifecycleStateAsync(model, gameServerData.GameServerId, cancellationToken).ConfigureAwait(false);
                if (!cod4xStatePreserved)
                {
                    ModelState.AddModelError(string.Empty, "Failed to load CoD4x plugin lifecycle state. Please retry.");
                    AddGameTypeViewData(model.GameServer.GameType);
                    AddPlatformViewData(model.GameServer.Platform);
                    await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                    return View(model);
                }
            }

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

            var modelValidationResult = CheckModelState(model, m =>
            {
                AddGameTypeViewData(m.GameServer.GameType);
                AddPlatformViewData(m.GameServer.Platform);
            });
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
                AddPlatformViewData(model.GameServer.Platform);
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
                && model.FileTransportConfigSftpAuthenticationType == SftpAuthenticationType.PrivateKey
                && string.IsNullOrWhiteSpace(model.FileTransportConfigPrivateKey))
            {
                ModelState.AddModelError(nameof(model.SftpConfigPrivateKey), "SFTP private key is required when private-key authentication is selected.");
                AddGameTypeViewData(model.GameServer.GameType);
                AddPlatformViewData(model.GameServer.Platform);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            if (canEditFileTransport.Succeeded
                && selectedFileTransportEnabled
                && selectedFileTransportType == FileTransportType.Sftp
                && string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint))
            {
                ModelState.AddModelError(nameof(model.FtpConfigHostKeyFingerprint), "SFTP host key fingerprint is required when SFTP is enabled.");
                AddGameTypeViewData(model.GameServer.GameType);
                AddPlatformViewData(model.GameServer.Platform);
                await RepopulateAuthFlags(model, gameServerData.GameType).ConfigureAwait(false);
                return View(model);
            }

            if (canEditFileTransport.Succeeded
                && !IsValidMapsRootPath(model.FileTransportConfigMapsRootPath))
            {
                ModelState.AddModelError(nameof(model.FtpConfigMapsRootPath), "Maps root path cannot contain path traversal segments.");
                AddGameTypeViewData(model.GameServer.GameType);
                AddPlatformViewData(model.GameServer.Platform);
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
                AddPlatformViewData(model.GameServer.Platform);
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

    private void AddPlatformViewData(GameServerPlatform? selected = null)
    {
        try
        {
            selected ??= GameServerPlatform.Unknown;

            var platforms = Enum.GetValues<GameServerPlatform>();
            ViewData[nameof(GameServerViewModel.Platform)] = new SelectList(platforms, selected);

            Logger.LogDebug("Added {PlatformCount} platforms to ViewData with {SelectedPlatform} selected",
                platforms.Length, selected);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding platform ViewData for user {UserId}", User.XtremeIdiotsId());

            ViewData[nameof(GameServerViewModel.Platform)] = new SelectList(Array.Empty<GameServerPlatform>(), selected ?? GameServerPlatform.Unknown);
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

    private async Task PopulateRuleCapabilitiesAsync(GameServerEditViewModel model, Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        RuleServerCapabilitiesDto? snapshot = null;
        try
        {
            var response = await repositoryApiClient.RuleConnectionBindings.V1
                .GetCurrentCapabilities(serverId, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess && response.Result?.Data is { } data && data.GameServerId == serverId && data.GameType == gameType)
                snapshot = data;
            else
                Logger.LogWarning("Cannot retrieve rule capability snapshot for game server {GameServerId}", serverId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Cannot retrieve rule capability snapshot for game server {GameServerId}", serverId);
        }

        model.HasFreshRuleCapabilitySnapshot = snapshot is not null && snapshot.HasFreshStatus && snapshot.ExpiresAtUtc > DateTimeOffset.UtcNow;
        model.VpnRuleCompatibility = RulesCapabilityResolver.Resolve(gameType, RulesEngineCatalogueV1.VpnProtectionFeature, snapshot);
        model.SiegeRuleCompatibility = RulesCapabilityResolver.Resolve(gameType, RulesEngineCatalogueV1.SiegeModeFeature, snapshot);
        model.WelcomeRuleCompatibility = RulesCapabilityResolver.Resolve(gameType, RulesEngineCatalogueV1.WelcomeMessagesFeature, snapshot);
    }

    private async Task<bool> PopulateGlobalDefaultsAsync(
        GameServerEditViewModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            var globalConfigsResult = await repositoryApiClient.GlobalConfigurations.V1
                .GetConfigurations(cancellationToken).ConfigureAwait(false);
            if (!globalConfigsResult.IsSuccess || globalConfigsResult.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve global configurations for server settings validation");
                return false;
            }

            foreach (var config in globalConfigsResult.Result.Data.Items)
            {
                if (string.Equals(config.Namespace, VpnProtectionSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    && !gameServerSettingsService.TryValidateGlobalVpnProtectionSettings(config, out var vpnErrors))
                {
                    Logger.LogWarning("Invalid global VPN Protection settings: {Errors}", string.Join("; ", vpnErrors));
                    return false;
                }

                if (string.Equals(config.Namespace, SiegeModeSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    && !gameServerSettingsService.TryValidateGlobalSiegeModeSettings(config, out var siegeErrors))
                {
                    Logger.LogWarning("Invalid global Siege Mode settings: {Errors}", string.Join("; ", siegeErrors));
                    return false;
                }

                if (string.Equals(config.Namespace, WelcomeRulesSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    && !gameServerSettingsService.TryValidateGlobalWelcomeRulesSettings(config, out var welcomeErrors))
                {
                    Logger.LogWarning("Invalid global Welcome Rules settings: {Errors}", string.Join("; ", welcomeErrors));
                    return false;
                }

                PopulateGlobalDefaults(model, config);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve global configurations for server settings validation");
            return false;
        }
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
        var isSftp = string.Equals(activeTransportNamespace, SftpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase);
        var usesPrivateKey = isSftp && model.FileTransportConfigSftpAuthenticationType == SftpAuthenticationType.PrivateKey;
        var needsFileTransportPassword = canEditFileTransport
            && !usesPrivateKey
            && string.IsNullOrEmpty(model.FileTransportConfigPassword);
        var needsFileTransportPrivateKey = canEditFileTransport
            && usesPrivateKey
            && string.IsNullOrEmpty(model.FileTransportConfigPrivateKey);
        var needsFileTransportPrivateKeyPassphrase = canEditFileTransport
            && usesPrivateKey
            && string.IsNullOrEmpty(model.FileTransportConfigPrivateKeyPassphrase);
        var needsFileTransportHostKeyFingerprint = canEditFileTransport
            && isSftp
            && string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint);
        var needsRconPassword = canEditRcon && string.IsNullOrEmpty(model.RconConfigPassword);
        var options = new CredentialPreservationOptions(
            needsFileTransportPassword,
            needsFileTransportPrivateKey,
            needsFileTransportPrivateKeyPassphrase,
            needsFileTransportHostKeyFingerprint,
            needsRconPassword);

        if (options is
            {
                FileTransportPassword: false,
                FileTransportPrivateKey: false,
                FileTransportPrivateKeyPassphrase: false,
                FileTransportHostKeyFingerprint: false,
                RconPassword: false
            })
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
                    options,
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

    private async Task<bool> PreserveServerManagedCod4xLifecycleStateAsync(
        GameServerEditViewModel model,
        Guid gameServerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                .GetConfigurations(gameServerId, cancellationToken).ConfigureAwait(false);

            if (!configsResult.IsSuccess || configsResult.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to load CoD4x lifecycle state for game server {GameServerId}", gameServerId);
                return false;
            }

            var cod4xPluginConfig = configsResult.Result.Data.Items.FirstOrDefault(static config =>
                string.Equals(config.Namespace, Cod4xPluginSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase));

            if (cod4xPluginConfig is null || string.IsNullOrWhiteSpace(cod4xPluginConfig.Configuration))
            {
                skipCod4xPluginNamespaceMutations = false;
                ResetCod4xServerManagedFields(model);
                return true;
            }

            if (!Cod4xPluginSettingsJsonHelper.TryDeserialize(
                cod4xPluginConfig.Configuration,
                cod4xPluginJsonOptions,
                out var cod4xPluginDocument)
                || cod4xPluginDocument is null)
            {
                skipCod4xPluginNamespaceMutations = true;
                Logger.LogWarning("Failed to parse CoD4x plugin lifecycle state for game server {GameServerId}. Skipping CoD4x plugin namespace updates for this request.", gameServerId);
                ResetCod4xServerManagedFields(model);
                return true;
            }

            skipCod4xPluginNamespaceMutations = false;

            model.Cod4xRuntimeCurrentVersion = cod4xPluginDocument.RuntimeState?.CurrentVersion;
            model.Cod4xRuntimePreviousKnownGoodVersion = cod4xPluginDocument.RuntimeState?.PreviousKnownGoodVersion;
            model.Cod4xRuntimeLastOperationId = cod4xPluginDocument.RuntimeState?.LastOperationId;
            model.Cod4xRuntimeLastOperationStatus = cod4xPluginDocument.RuntimeState?.LastOperationStatus ?? Cod4xPluginOperationStatus.Unknown;
            model.Cod4xRuntimeLastOperationUtc = cod4xPluginDocument.RuntimeState?.LastOperationUtc;
            model.Cod4xRuntimeLastError = cod4xPluginDocument.RuntimeState?.LastError;
            model.Cod4xOperationRequestOperationId = cod4xPluginDocument.OperationRequest?.OperationId;
            model.Cod4xOperationRequestAction = cod4xPluginDocument.OperationRequest?.Action ?? Cod4xPluginOperationAction.Unknown;
            model.Cod4xOperationRequestTargetVersion = cod4xPluginDocument.OperationRequest?.TargetVersion;
            model.Cod4xOperationRequestRequestedAtUtc = cod4xPluginDocument.OperationRequest?.RequestedAtUtc;
            model.Cod4xOperationRequestRequestedBy = cod4xPluginDocument.OperationRequest?.RequestedBy;

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to preserve CoD4x lifecycle state for game server {GameServerId}", gameServerId);
            return false;
        }
    }

    private static void ResetCod4xServerManagedFields(GameServerEditViewModel model)
    {
        model.Cod4xRuntimeCurrentVersion = null;
        model.Cod4xRuntimePreviousKnownGoodVersion = null;
        model.Cod4xRuntimeLastOperationId = null;
        model.Cod4xRuntimeLastOperationStatus = Cod4xPluginOperationStatus.Unknown;
        model.Cod4xRuntimeLastOperationUtc = null;
        model.Cod4xRuntimeLastError = null;
        model.Cod4xOperationRequestOperationId = null;
        model.Cod4xOperationRequestAction = Cod4xPluginOperationAction.Unknown;
        model.Cod4xOperationRequestTargetVersion = null;
        model.Cod4xOperationRequestRequestedAtUtc = null;
        model.Cod4xOperationRequestRequestedBy = null;
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
        try
        {
            var serverTitle = model.GameServer.Title ?? "";
            var namespacesToUpsert = gameServerSettingsService.BuildNamespaceConfigurations(model, canEditFileTransport, canEditRcon, canConfigureScreenshots);
            var upsertedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (ns, json) in namespacesToUpsert)
            {
                if (skipCod4xPluginNamespaceMutations && string.Equals(ns, Cod4xPluginSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                upsertedNamespaces.Add(ns);
                await UpsertConfigSafeAsync(gameServerId, ns, json, serverTitle, errors, cancellationToken).ConfigureAwait(false);
            }

            foreach (var ns in gameServerSettingsService.DeletedNamespaces)
            {
                if (skipCod4xPluginNamespaceMutations && string.Equals(ns, Cod4xPluginSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (upsertedNamespaces.Contains(ns))
                {
                    continue;
                }

                await DeleteConfigSafeAsync(gameServerId, ns, errors, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            skipCod4xPluginNamespaceMutations = false;
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

            if (!result.IsSuccess
                && !result.IsNotFound
                && result.StatusCode != System.Net.HttpStatusCode.NoContent)
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
                    TagId = tag.TagId,
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
