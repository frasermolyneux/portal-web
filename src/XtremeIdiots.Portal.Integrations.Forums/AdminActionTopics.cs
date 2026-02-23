using Microsoft.Extensions.Configuration;
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
public class AdminActionTopics(ILogger<AdminActionTopics> logger, IInvisionApiClient forumsClient, IConfiguration configuration) : IAdminActionTopics
{

    /// <summary>
    /// Creates a forum topic for a new admin action
    /// </summary>
    public async Task<int> CreateTopicForAdminAction(AdminActionType type, GameType gameType, Guid playerId, string username, DateTime created, string text, string? adminId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = int.Parse(configuration["XtremeIdiots:Forums:DefaultAdminUserId"] ?? "21145");

            if (adminId is not null)
                userId = Convert.ToInt32(adminId);

            var forumId = ResolveForumId(type, gameType);

            var postTopicResult = await forumsClient.Forums.PostTopic(forumId, userId, $"{username} - {type}", PostContent(type, playerId, username, created, text), type.ToString(), cancellationToken).ConfigureAwait(false);

            if (postTopicResult?.Result?.Data is null)
            {
                logger.LogWarning("Failed to create forum topic for admin action - StatusCode: {StatusCode}, Errors: {Errors}",
                    postTopicResult?.StatusCode,
                    postTopicResult?.Result?.Errors is { Length: > 0 } errors
                        ? string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"))
                        : "no error details available");
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

        var userId = int.Parse(configuration["XtremeIdiots:Forums:DefaultAdminUserId"] ?? "21145");

        if (adminId is not null)
            userId = Convert.ToInt32(adminId);

        await forumsClient.Forums.UpdateTopic(topicId, userId, PostContent(type, playerId, username, created, text), cancellationToken).ConfigureAwait(false);
    }

    private string PostContent(AdminActionType type, Guid playerId, string username, DateTime created, string text)
    {
        var portalBaseUrl = (configuration["XtremeIdiots:PortalBaseUrl"] ?? "https://portal.xtremeidiots.com").TrimEnd('/');
        return "<p>" +
               $"   Username: {username}<br>" +
               $"   Player Link: <a href=\"{portalBaseUrl}/Players/Details/{playerId}\">Portal</a><br>" +
               $"   {type} Created: {created.ToString(CultureInfo.InvariantCulture)}" +
               "</p>" +
               "<p>" +
               $"   {text}" +
               "</p>" +
               "<p>" +
               "   <small>Do not edit this post directly as it will be overwritten by the Portal. Add comments on posts below or edit the record in the Portal.</small>" +
               "</p>";
    }

    private int ResolveForumId(AdminActionType type, GameType gameType)
    {
        var defaultForumId = int.Parse(configuration["XtremeIdiots:Forums:DefaultForumId"] ?? "28");

        var category = type switch
        {
            AdminActionType.Observation or AdminActionType.Warning or AdminActionType.Kick => "AdminLogs",
            AdminActionType.TempBan or AdminActionType.Ban => "Bans",
            _ => null
        };

        if (category is null)
            return defaultForumId;

        var gameKey = gameType switch
        {
            GameType.Arma or GameType.Arma2 or GameType.Arma3 => "Arma",
            _ => gameType.ToString()
        };

        var configValue = configuration[$"XtremeIdiots:Forums:{category}:{gameKey}"];
        if (configValue is not null && int.TryParse(configValue, out var forumId))
            return forumId;

        // Fallback to hardcoded values from GameTypeExtensions
        return type switch
        {
            AdminActionType.Observation => gameType.ForumIdForObservations(),
            AdminActionType.Warning => gameType.ForumIdForWarnings(),
            AdminActionType.Kick => gameType.ForumIdForKicks(),
            AdminActionType.TempBan => gameType.ForumIdForTempBans(),
            AdminActionType.Ban => gameType.ForumIdForBans(),
            _ => defaultForumId
        };
    }
}