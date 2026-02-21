using Microsoft.Extensions.Logging;
using System.Globalization;
using MX.InvisionCommunity.Api.Abstractions;
using XtremeIdiots.Portal.Integrations.Forums.Extensions;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Integrations.Forums;

/// <summary>
/// Manages forum topic creation and updates for admin actions in the XtremeIdiots community forums
/// </summary>
/// <param name="logger">Logger for tracking operations and errors</param>
/// <param name="forumsClient">Invision Community API client for forum operations</param>
public class AdminActionTopics(ILogger<AdminActionTopics> logger, IInvisionApiClient forumsClient) : IAdminActionTopics
{

    /// <summary>
    /// Creates a forum topic for a new admin action
    /// </summary>
    /// <param name="type">Type of admin action (warning, ban, etc.)</param>
    /// <param name="gameType">Game type to determine appropriate forum section</param>
    /// <param name="playerId">Unique identifier of the player</param>
    /// <param name="username">Player's username</param>
    /// <param name="created">When the admin action was created</param>
    /// <param name="text">Admin action description/reason</param>
    /// <param name="adminId">ID of the admin who created the action</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Topic ID of the created forum topic, or 0 if creation failed</returns>
    public async Task<int> CreateTopicForAdminAction(AdminActionType type, GameType gameType, Guid playerId, string username, DateTime created, string text, string? adminId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = 21145;

            if (adminId is not null)
                userId = Convert.ToInt32(adminId);

            var forumId = type switch
            {
                AdminActionType.Observation => gameType.ForumIdForObservations(),
                AdminActionType.Warning => gameType.ForumIdForWarnings(),
                AdminActionType.Kick => gameType.ForumIdForKicks(),
                AdminActionType.TempBan => gameType.ForumIdForTempBans(),
                AdminActionType.Ban => gameType.ForumIdForBans(),
                _ => 28
            };

            var postTopicResult = await forumsClient.Forums.PostTopic(forumId, userId, $"{username} - {type}", PostContent(type, playerId, username, created, text), type.ToString()).ConfigureAwait(false);

            if (postTopicResult?.Result?.Data is null)
            {
                logger.LogWarning("Failed to create forum topic for admin action - null response");
                return 0;
            }

            return postTopicResult.Result.Data.TopicId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating admin action topic");
            return 0;
        }
    }

    /// <summary>
    /// Updates an existing forum topic with new admin action information
    /// </summary>
    /// <param name="topicId">ID of the forum topic to update</param>
    /// <param name="type">Type of admin action</param>
    /// <param name="gameType">Game type (not used in current implementation)</param>
    /// <param name="playerId">Unique identifier of the player</param>
    /// <param name="username">Player's username</param>
    /// <param name="created">When the admin action was created</param>
    /// <param name="text">Admin action description/reason</param>
    /// <param name="adminId">ID of the admin who created the action</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    public async Task UpdateTopicForAdminAction(int topicId, AdminActionType type, GameType gameType, Guid playerId, string username, DateTime created, string text, string? adminId, CancellationToken cancellationToken = default)
    {
        if (topicId == 0)
            return;

        var userId = 21145;

        if (adminId is not null)
            userId = Convert.ToInt32(adminId);

        await forumsClient.Forums.UpdateTopic(topicId, userId, PostContent(type, playerId, username, created, text)).ConfigureAwait(false);
    }

    private static string PostContent(AdminActionType type, Guid playerId, string username, DateTime created, string text)
    {
        return "<p>" +
               $"   Username: {username}<br>" +
               $"   Player Link: <a href=\"https://portal.xtremeidiots.com/Players/Details/{playerId}\">Portal</a><br>" +
               $"   {type} Created: {created.ToString(CultureInfo.InvariantCulture)}" +
               "</p>" +
               "<p>" +
               $"   {text}" +
               "</p>" +
               "<p>" +
               "   <small>Do not edit this post directly as it will be overwritten by the Portal. Add comments on posts below or edit the record in the Portal.</small>" +
               "</p>";
    }
}