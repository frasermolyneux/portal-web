using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Services;

public class NotificationDispatcher(
    IRepositoryApiClient repositoryApiClient,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private readonly static string[] adminClaimTypes =
    [
        UserProfileClaimType.SeniorAdmin,
        UserProfileClaimType.HeadAdmin,
        UserProfileClaimType.GameAdmin,
        UserProfileClaimType.Moderator
    ];

    public async Task DispatchAdminActionCreatedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default)
    {
        var title = $"New {context.ActionType} on {context.GameType.ToDisplayName()}";
        var message = context.AdminDisplayName is not null
            ? $"{context.AdminDisplayName} added a {context.ActionType} to {context.PlayerName}"
            : $"A {context.ActionType} has been added to {context.PlayerName}";
        var actionUrl = $"/Players/Details/{context.PlayerId}";

        await DispatchToGameTypeAdminsAsync(
            "admin-action-new",
            context.GameType,
            context.AdminUserProfileId,
            title,
            message,
            actionUrl,
            BuildMetadata(context),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchAdminActionClaimedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default)
    {
        var title = $"{context.ActionType} Claimed on {context.GameType.ToDisplayName()}";
        var message = context.AdminDisplayName is not null
            ? $"{context.AdminDisplayName} claimed a {context.ActionType} on {context.PlayerName}"
            : $"A {context.ActionType} on {context.PlayerName} has been claimed";
        var actionUrl = $"/Players/Details/{context.PlayerId}";

        await DispatchToGameTypeAdminsAsync(
            "admin-action-claimed",
            context.GameType,
            context.AdminUserProfileId,
            title,
            message,
            actionUrl,
            BuildMetadata(context),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchAdminActionLiftedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default)
    {
        var title = $"{context.ActionType} Lifted on {context.GameType.ToDisplayName()}";
        var message = context.AdminDisplayName is not null
            ? $"{context.AdminDisplayName} lifted a {context.ActionType} on {context.PlayerName}"
            : $"A {context.ActionType} on {context.PlayerName} has been lifted";
        var actionUrl = $"/Players/Details/{context.PlayerId}";

        await DispatchToGameTypeAdminsAsync(
            "admin-action-lifted",
            context.GameType,
            context.AdminUserProfileId,
            title,
            message,
            actionUrl,
            BuildMetadata(context),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchToGameTypeAdminsAsync(
        string notificationTypeId,
        GameType gameType,
        Guid? excludeUserProfileId,
        string title,
        string message,
        string actionUrl,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var recipientIds = await ResolveGameTypeRecipientIdsAsync(gameType, cancellationToken).ConfigureAwait(false);

            // Exclude the admin who performed the action (they don't need to be notified of their own action)
            if (excludeUserProfileId.HasValue)
                recipientIds.Remove(excludeUserProfileId.Value);

            if (recipientIds.Count == 0)
            {
                logger.LogDebug("No recipients found for notification type {NotificationTypeId} on game type {GameType}", notificationTypeId, gameType);
                return;
            }

            logger.LogInformation("Dispatching {NotificationTypeId} notification to {RecipientCount} recipients for {GameType}",
                notificationTypeId, recipientIds.Count, gameType);

            // Create notifications for each recipient
            // Note: per-user preference checking is deferred until a bulk preferences API is available;
            // preferences default to enabled so all recipients receive notifications initially.
            foreach (var recipientId in recipientIds)
            {
                var dto = new CreateNotificationDto(recipientId, notificationTypeId, title, message)
                {
                    ActionUrl = actionUrl,
                    MetadataJson = metadataJson
                };

                await repositoryApiClient.Notifications.V1.CreateNotification(dto, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Notification failures should not break the primary operation
            logger.LogError(ex, "Failed to dispatch {NotificationTypeId} notifications for {GameType}", notificationTypeId, gameType);
        }
    }

    private async Task<HashSet<Guid>> ResolveGameTypeRecipientIdsAsync(GameType gameType, CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<Guid>();
        var gameTypeString = gameType.ToString();

        // Fetch all admin users (with claims included)
        const int pageSize = 500;
        var result = await repositoryApiClient.UserProfiles.V1
            .GetUserProfiles(null, UserProfileFilter.AnyAdmin, 0, pageSize, null, cancellationToken)
            .ConfigureAwait(false);

        if (result.Result?.Data?.Items is null)
            return recipientIds;

        var items = result.Result.Data.Items;
        if (items.Count() >= pageSize)
            logger.LogWarning("Admin user query returned {Count} results (page limit {PageSize}); some admins may not receive notifications", items.Count(), pageSize);

        foreach (var userProfile in items)
        {
            // SeniorAdmins get notifications for all game types
            var isSeniorAdmin = userProfile.UserProfileClaims
                .Any(c => c.ClaimType == UserProfileClaimType.SeniorAdmin);

            if (isSeniorAdmin)
            {
                recipientIds.Add(userProfile.UserProfileId);
                continue;
            }

            // Other admin roles are game-type scoped (claim value = game type string)
            var hasGameTypeClaim = userProfile.UserProfileClaims
                .Any(c => adminClaimTypes.Contains(c.ClaimType) && c.ClaimValue == gameTypeString);

            if (hasGameTypeClaim)
                recipientIds.Add(userProfile.UserProfileId);
        }

        return recipientIds;
    }

    private static string BuildMetadata(AdminActionNotificationContext context)
    {
        return JsonSerializer.Serialize(new
        {
            gameType = context.GameType.ToString(),
            actionType = context.ActionType.ToString(),
            playerName = context.PlayerName,
            playerId = context.PlayerId,
            adminDisplayName = context.AdminDisplayName
        });
    }
}


