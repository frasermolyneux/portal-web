using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing admin actions against players in the gaming portal
/// </summary>
[Authorize(Policy = AuthPolicies.AccessAdminActionsController)]
public class AdminActionsController(
    IAuthorizationService authorizationService,
    IAdminActionTopics adminActionTopics,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<AdminActionsController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the create admin action form for a specific player
    /// </summary>
    /// <param name="id">The player ID</param>
    /// <param name="adminActionType">The type of admin action to create</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The create admin action view</returns>
    [HttpGet]
    public async Task<IActionResult> Create(Guid id, AdminActionType adminActionType, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var playerData = await GetPlayerDataAsync(id, cancellationToken).ConfigureAwait(false);
            if (playerData is null)
                return NotFound();

            var authorizationResource = (playerData.GameType, adminActionType);

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.CreateAdminAction,
                "Create",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionType:{adminActionType}",
                playerData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var tempBanDurationDays = int.TryParse(Configuration["XtremeIdiots:Forums:DefaultTempBanDays"], out var days) ? days : 7;

            var createAdminActionViewModel = new CreateAdminActionViewModel
            {
                Type = adminActionType,
                PlayerId = playerData.PlayerId,
                PlayerDto = playerData,
                Expires = adminActionType == AdminActionType.TempBan ? DateTime.UtcNow.AddDays(tempBanDurationDays) : null
            };

            return View(createAdminActionViewModel);
        }, $"CreateAdminActionForm-{adminActionType}").ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new admin action for the specified player
    /// </summary>
    /// <param name="model">The create admin action view model containing form data</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details on success, returns view with validation errors on failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateAdminActionViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var playerData = await GetPlayerDataAsync(model.PlayerId, cancellationToken).ConfigureAwait(false);
            if (playerData is null)
                return NotFound();

            var modelValidationResult = CheckModelState(model, m => m.PlayerDto = playerData);
            if (modelValidationResult is not null)
                return modelValidationResult;

            var authorizationResource = (playerData.GameType, model.Type);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.CreateAdminAction,
                "Create",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionType:{model.Type}",
                playerData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var adminId = User.XtremeIdiotsId();
            var createAdminActionDto = new CreateAdminActionDto(playerData.PlayerId, model.Type, model.Text)
            {
                AdminId = adminId,
                Expires = model.Expires,
                ForumTopicId = await adminActionTopics.CreateTopicForAdminAction(
                    model.Type,
                    playerData.GameType,
                    playerData.PlayerId,
                    playerData.Username,
                    DateTime.UtcNow,
                    model.Text,
                    adminId,
                    cancellationToken).ConfigureAwait(false)
            };

            await repositoryApiClient.AdminActions.V1.CreateAdminAction(createAdminActionDto, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("AdminActionCreated", "CreateAdminAction", new Dictionary<string, string>
            {
                { "PlayerId", model.PlayerId.ToString() },
                { "AdminActionType", model.Type.ToString() },
                { "ForumTopicId", createAdminActionDto.ForumTopicId?.ToString() ?? string.Empty }
            });

            this.AddAlertSuccess(CreateActionAppliedMessage(model.Type, playerData.Username, createAdminActionDto.ForumTopicId));

            return RedirectToAction("Details", "Players", new { id = model.PlayerId });
        }, $"CreateAdminAction-{model.Type}").ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the edit admin action form
    /// </summary>
    /// <param name="id">The admin action ID to edit</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The edit admin action view</returns>
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var adminActionData = await GetAdminActionDataAsync(id, cancellationToken).ConfigureAwait(false);
            if (adminActionData is null)
                return NotFound();

            var playerData = adminActionData.Player!;

            var authorizationResource = (playerData.GameType, adminActionData.Type, adminActionData.UserProfile?.XtremeIdiotsForumId);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.EditAdminAction,
                "Edit",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{id}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var viewModel = new EditAdminActionViewModel
            {
                AdminActionId = adminActionData.AdminActionId,
                PlayerId = adminActionData.PlayerId,
                Type = adminActionData.Type,
                Text = adminActionData.Text,
                Expires = adminActionData.Expires,
                AdminId = adminActionData.UserProfile?.XtremeIdiotsForumId,
                PlayerDto = playerData
            };

            return View(viewModel);
        }, "EditAdminActionForm").ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing admin action with new information
    /// </summary>
    /// <param name="model">The edit admin action view model containing updated data</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details on success, returns view with validation errors on failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditAdminActionViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var adminActionData = await GetAdminActionDataAsync(model.AdminActionId, cancellationToken).ConfigureAwait(false);
            if (adminActionData is null)
                return NotFound();

            var playerData = adminActionData.Player!;

            var modelValidationResult = CheckModelState(model, m => m.PlayerDto = playerData);
            if (modelValidationResult is not null)
                return modelValidationResult;

            var authorizationResource = (playerData.GameType, adminActionData.Type, adminActionData.UserProfile?.XtremeIdiotsForumId);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.EditAdminAction,
                "Edit",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{model.AdminActionId}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var editAdminActionDto = new EditAdminActionDto(adminActionData.AdminActionId)
            {
                Text = model.Text,
                Expires = model.Type == AdminActionType.TempBan ? model.Expires : null
            };

            var canChangeAdminActionAdmin = await authorizationService.AuthorizeAsync(User, playerData.GameType, AuthPolicies.ChangeAdminActionAdmin).ConfigureAwait(false);

            if (canChangeAdminActionAdmin.Succeeded && adminActionData.UserProfile?.XtremeIdiotsForumId != model.AdminId)
            {
                editAdminActionDto.AdminId = string.IsNullOrWhiteSpace(model.AdminId) ? GetFallbackAdminId() : model.AdminId;
                Logger.LogInformation("User {UserId} changed admin for action {AdminActionId} to {NewAdminId}",
                    User.XtremeIdiotsId(), model.AdminActionId, editAdminActionDto.AdminId);
            }

            await repositoryApiClient.AdminActions.V1.UpdateAdminAction(editAdminActionDto, cancellationToken).ConfigureAwait(false);

            var adminForumId = canChangeAdminActionAdmin.Succeeded && adminActionData.UserProfile?.XtremeIdiotsForumId != model.AdminId
                ? editAdminActionDto.AdminId
                : adminActionData.UserProfile?.XtremeIdiotsForumId;

            await UpdateForumTopicIfExistsAsync(adminActionData, model.Text, adminForumId, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("AdminActionEdited", nameof(Edit), new Dictionary<string, string>
            {
                { nameof(model.AdminActionId), model.AdminActionId.ToString() },
                { nameof(model.PlayerId), model.PlayerId.ToString() },
                { "AdminActionType", model.Type.ToString() }
            });

            this.AddAlertSuccess(CreateActionOperationMessage(model.Type, playerData.Username, "updated"));

            return RedirectToAction(nameof(PlayersController.Details), "Players", new { id = model.PlayerId });
        }, nameof(Edit)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the lift admin action confirmation form
    /// </summary>
    /// <param name="id">The admin action ID to lift</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The lift confirmation view</returns>
    [HttpGet]
    public async Task<IActionResult> Lift(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var adminActionData = await GetAdminActionDataAsync(id, cancellationToken).ConfigureAwait(false);
            if (adminActionData is null)
                return NotFound();

            var playerData = adminActionData.Player!;

            var authorizationResource = (playerData.GameType, adminActionData.UserProfile?.XtremeIdiotsForumId);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.LiftAdminAction,
                "Lift",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{id}",
                adminActionData).ConfigureAwait(false);

            return authResult is not null ? authResult : View(adminActionData);
        }, "LiftAdminActionForm").ConfigureAwait(false);
    }

    /// <summary>
    /// Lifts (expires) an admin action immediately
    /// </summary>
    /// <param name="id">The admin action ID to lift</param>
    /// <param name="playerId">The player ID associated with the admin action</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details after lifting the action</returns>
    [HttpPost]
    [ActionName(nameof(Lift))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LiftConfirmed(Guid id, Guid playerId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var adminActionData = await GetAdminActionDataAsync(id, cancellationToken).ConfigureAwait(false);
            if (adminActionData is null)
                return NotFound();

            var playerData = adminActionData.Player!;

            var authorizationResource = (playerData.GameType, adminActionData.UserProfile?.XtremeIdiotsForumId);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.LiftAdminAction,
                "Lift",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{id},PlayerId:{playerId}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var editAdminActionDto = new EditAdminActionDto(adminActionData.AdminActionId)
            {
                Expires = DateTime.UtcNow
            };

            await repositoryApiClient.AdminActions.V1.UpdateAdminAction(editAdminActionDto, cancellationToken).ConfigureAwait(false);

            await UpdateForumTopicIfExistsAsync(adminActionData, adminActionData.Text, adminActionData.UserProfile?.XtremeIdiotsForumId, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("AdminActionLifted", nameof(Lift), new Dictionary<string, string>
            {
                { "AdminActionId", id.ToString() },
                { nameof(playerId), playerId.ToString() },
                { "AdminActionType", adminActionData.Type.ToString() }
            });

            this.AddAlertSuccess(CreateActionOperationMessage(adminActionData.Type, playerData.Username, "lifted"));

            return RedirectToAction(nameof(PlayersController.Details), "Players", new { id = playerId });
        }, nameof(Lift)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the claim admin action confirmation form
    /// </summary>
    /// <param name="id">The admin action ID to claim</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The claim confirmation view</returns>
    [HttpGet]
    public async Task<IActionResult> Claim(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);

            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for claim operation", id);
                return NotFound();
            }

            var adminActionData = getAdminActionResult.Result.Data;

            // Ensure player data is available (some API responses may omit nested player details)
            var playerData = adminActionData.Player;
            if (playerData is null)
            {
                var playerResult = await repositoryApiClient.Players.V1.GetPlayer(adminActionData.PlayerId, PlayerEntityOptions.None).ConfigureAwait(false);
                if (playerResult.IsNotFound || playerResult.Result?.Data is null)
                {
                    Logger.LogWarning("Player {PlayerId} not found when enriching admin action {AdminActionId} for claim operation", adminActionData.PlayerId, id);
                    return NotFound();
                }
                playerData = playerResult.Result.Data;
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                playerData.GameType,
                AuthPolicies.ClaimAdminAction,
                "Claim",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{id}",
                adminActionData).ConfigureAwait(false);

            return authResult is not null ? authResult : View(adminActionData);
        }, "ClaimAdminActionForm").ConfigureAwait(false);
    }

    /// <summary>
    /// Claims an admin action for the current user
    /// </summary>
    /// <param name="id">The admin action ID to claim</param>
    /// <param name="playerId">The player ID associated with the admin action</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details after claiming the action</returns>
    [HttpPost]
    [ActionName(nameof(Claim))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaimConfirmed(Guid id, Guid playerId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);

            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data?.Player is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for claim operation", id);
                return NotFound();
            }

            var adminActionData = getAdminActionResult.Result.Data;
            var playerData = adminActionData.Player;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                playerData.GameType,
                AuthPolicies.ClaimAdminAction,
                "Claim",
                "AdminActions",
                $"GameType:{playerData.GameType},AdminActionId:{id},PlayerId:{playerId}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var adminId = User.XtremeIdiotsId();
            var editAdminActionDto = new EditAdminActionDto(adminActionData.AdminActionId)
            {
                AdminId = adminId
            };

            await repositoryApiClient.AdminActions.V1.UpdateAdminAction(editAdminActionDto, cancellationToken).ConfigureAwait(false);

            if (adminActionData.ForumTopicId.HasValue && adminActionData.ForumTopicId != 0)
            {
                await adminActionTopics.UpdateTopicForAdminAction(
                    adminActionData.ForumTopicId.Value,
                    adminActionData.Type,
                    playerData.GameType,
                    playerData.PlayerId,
                    playerData.Username,
                    adminActionData.Created,
                    adminActionData.Text,
                    adminId).ConfigureAwait(false);
            }

            TrackSuccessTelemetry("AdminActionClaimed", nameof(Claim), new Dictionary<string, string>
            {
                { "AdminActionId", id.ToString() },
                { nameof(playerId), playerId.ToString() },
                { "AdminActionType", adminActionData.Type.ToString() }
            });

            this.AddAlertSuccess($"The {adminActionData.Type} has been successfully claimed for {playerData.Username}");

            return RedirectToAction(nameof(PlayersController.Details), "Players", new { id = playerId });
        }, nameof(Claim)).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a forum discussion topic for an existing admin action
    /// </summary>
    /// <param name="id">The admin action ID to create a topic for</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details after creating the topic</returns>
    [HttpGet]
    public async Task<IActionResult> CreateDiscussionTopic(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);

            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data?.Player is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for discussion topic creation", id);
                return NotFound();
            }

            var adminActionData = getAdminActionResult.Result.Data;
            var playerData = adminActionData.Player;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                playerData.GameType,
                AuthPolicies.CreateAdminActionTopic,
                "CreateDiscussionTopic",
                "AdminActionTopic",
                $"GameType:{playerData.GameType},AdminActionId:{id}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var forumTopicId = await adminActionTopics.CreateTopicForAdminAction(
                adminActionData.Type,
                playerData.GameType,
                playerData.PlayerId,
                playerData.Username,
                DateTime.UtcNow,
                adminActionData.Text,
                adminActionData.UserProfile?.XtremeIdiotsForumId,
                cancellationToken).ConfigureAwait(false);

            var editAdminActionDto = new EditAdminActionDto(adminActionData.AdminActionId)
            {
                ForumTopicId = forumTopicId
            };

            await repositoryApiClient.AdminActions.V1.UpdateAdminAction(editAdminActionDto, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("AdminActionTopicCreated", nameof(CreateDiscussionTopic), new Dictionary<string, string>
            {
                { "AdminActionId", id.ToString() },
                { "ForumTopicId", forumTopicId.ToString() },
                { nameof(adminActionData.PlayerId), adminActionData.PlayerId.ToString() }
            });

            var forumBaseUrl = GetForumBaseUrl();
            this.AddAlertSuccess($"The discussion topic has been successfully created <a target=\"_blank\" href=\"{forumBaseUrl}{forumTopicId}-topic/\" class=\"alert-link\">here</a>");

            return RedirectToAction(nameof(PlayersController.Details), "Players", new { id = adminActionData.PlayerId });
        }, nameof(CreateDiscussionTopic)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the delete admin action confirmation form
    /// </summary>
    /// <param name="id">The admin action ID to delete</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The delete confirmation view</returns>
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);

            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data?.Player is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for delete operation", id);
                return NotFound();
            }

            var adminActionData = getAdminActionResult.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                adminActionData,
                AuthPolicies.DeleteAdminAction,
                "Delete",
                "AdminActions",
                $"AdminActionId:{id}",
                adminActionData).ConfigureAwait(false);

            return authResult is not null ? authResult : View(adminActionData);
        }, "DeleteAdminActionForm").ConfigureAwait(false);
    }

    /// <summary>
    /// Permanently deletes an admin action
    /// </summary>
    /// <param name="id">The admin action ID to delete</param>
    /// <param name="playerId">The player ID associated with the admin action</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to player details after deleting the action</returns>
    [HttpPost]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, Guid playerId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);

            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data?.Player is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for delete operation", id);
                return NotFound();
            }

            var adminActionData = getAdminActionResult.Result.Data;
            var playerData = adminActionData.Player;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                adminActionData,
                AuthPolicies.DeleteAdminAction,
                "Delete",
                "AdminActions",
                $"AdminActionId:{id},PlayerId:{playerId}",
                adminActionData).ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            await repositoryApiClient.AdminActions.V1.DeleteAdminAction(id, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("AdminActionDeleted", nameof(Delete), new Dictionary<string, string>
            {
                { "AdminActionId", id.ToString() },
                { nameof(playerId), playerId.ToString() },
                { "AdminActionType", adminActionData.Type.ToString() }
            });

            this.AddAlertSuccess($"The {adminActionData.Type} has been successfully deleted from {playerData.Username}");

            return RedirectToAction(nameof(PlayersController.Details), "Players", new { id = playerId });
        }, nameof(Delete)).ConfigureAwait(false);
    }

    private string GetForumBaseUrl()
    {
        return (Configuration["XtremeIdiots:Forums:TopicBaseUrl"] ?? "https://www.xtremeidiots.com/forums/topic/").TrimEnd('/') + "/";
    }

    private string GetFallbackAdminId()
    {
        return Configuration["XtremeIdiots:Forums:DefaultAdminUserId"] ?? "21145";
    }

    private async Task<PlayerDto?> GetPlayerDataAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var getPlayerResult = await repositoryApiClient.Players.V1.GetPlayer(playerId, PlayerEntityOptions.None).ConfigureAwait(false);

        if (getPlayerResult.IsNotFound)
        {
            Logger.LogWarning("Player {PlayerId} not found", playerId);
            return null;
        }

        if (getPlayerResult.Result?.Data is null)
        {
            Logger.LogWarning("Player data is null for {PlayerId}", playerId);
            throw new InvalidOperationException($"Player data retrieval failed for ID: {playerId}");
        }

        return getPlayerResult.Result.Data;
    }

    private async Task<AdminActionDto?> GetAdminActionDataAsync(Guid adminActionId, CancellationToken cancellationToken = default)
    {
        var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(adminActionId, cancellationToken).ConfigureAwait(false);

        if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data?.Player is null)
        {
            Logger.LogWarning("Admin action {AdminActionId} not found or has no associated player", adminActionId);
            return null;
        }

        return getAdminActionResult.Result.Data;
    }

    private async Task UpdateForumTopicIfExistsAsync(AdminActionDto adminActionData, string text, string? adminForumId, CancellationToken cancellationToken = default)
    {
        if (adminActionData.ForumTopicId.HasValue && adminActionData.ForumTopicId != 0 && adminActionData.Player is not null)
        {
            await adminActionTopics.UpdateTopicForAdminAction(
                adminActionData.ForumTopicId.Value,
                adminActionData.Type,
                adminActionData.Player.GameType,
                adminActionData.Player.PlayerId,
                adminActionData.Player.Username,
                adminActionData.Created,
                text,
                adminForumId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private string CreateActionAppliedMessage(AdminActionType actionType, string username, int? forumTopicId)
    {
        var forumBaseUrl = GetForumBaseUrl();
        return $"The {actionType} has been successfully applied against {username} with a <a target=\"_blank\" href=\"{forumBaseUrl}{forumTopicId}-topic/\" class=\"alert-link\">topic</a>";
    }

    private static string CreateActionOperationMessage(AdminActionType actionType, string username, string operation)
    {
        return $"The {actionType} has been successfully {operation} for {username}";
    }

    /// <summary>
    /// Displays admin actions created by the current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with the user's admin actions</returns>
    [HttpGet]
    public async Task<IActionResult> MyActions(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var adminActionsApiResponse = await repositoryApiClient.AdminActions.V1.GetAdminActions(null, null, User.XtremeIdiotsId(), null, 0, 50, AdminActionOrder.CreatedDesc).ConfigureAwait(false);

            if (!adminActionsApiResponse.IsSuccess || adminActionsApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve admin actions for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction("Display", "Errors", new { id = 500 });
            }

            Logger.LogInformation("Successfully retrieved {Count} admin actions for user {UserId}",
                adminActionsApiResponse.Result.Data.Items.Count(), User.XtremeIdiotsId());

            return View(adminActionsApiResponse.Result.Data.Items.ToList());
        }, nameof(MyActions)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays unclaimed admin actions (bans without assigned administrators)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with unclaimed admin actions</returns>
    [HttpGet]
    public async Task<IActionResult> Unclaimed(CancellationToken cancellationToken = default)
    {
        // New DataTables based view does not require pre-loaded model (AJAX fetch)
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            await Task.CompletedTask.ConfigureAwait(false); // maintain async signature for consistency
            return View();
        }, nameof(Unclaimed)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays a global list of admin actions with client-side filtering and sorting capabilities.
    /// </summary>
    /// <returns>View containing a table of recent admin actions</returns>
    [HttpGet]
    public IActionResult Global()
    {
        return View();
    }

    /// <summary>
    /// Returns a partial view with full details for an admin action (used in My Actions details panel)
    /// </summary>
    /// <param name="id">Admin action id</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Partial HTML</returns>
    [HttpGet]
    public async Task<IActionResult> GetMyAdminActionDetails(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var getAdminActionResult = await repositoryApiClient.AdminActions.V1.GetAdminAction(id, cancellationToken).ConfigureAwait(false);
            if (getAdminActionResult.IsNotFound || getAdminActionResult.Result?.Data is null)
            {
                Logger.LogWarning("Admin action {AdminActionId} not found for my details panel", id);
                return NotFound();
            }

            var adminAction = getAdminActionResult.Result.Data;
            PlayerDto? player = null;

            var playerResult = await repositoryApiClient.Players.V1.GetPlayer(adminAction.PlayerId, PlayerEntityOptions.None).ConfigureAwait(false);
            if (!playerResult.IsNotFound && playerResult.Result?.Data is not null)
            {
                player = playerResult.Result.Data;
            }

            var vm = new MyAdminActionDetailsViewModel(adminAction, player);
            return PartialView("_MyAdminActionDetailsPanel", vm);
        }, nameof(GetMyAdminActionDetails)).ConfigureAwait(false);
    }

    // Recent admin actions feature removed (was action: Recent) as per maintenance decision.
}