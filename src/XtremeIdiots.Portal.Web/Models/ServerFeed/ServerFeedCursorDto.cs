namespace XtremeIdiots.Portal.Web.Models.ServerFeed;

public class ServerFeedCursorDto
{
    public DateTime? LastSeenTimestampUtc { get; init; }
    public string? LastSeenSourceType { get; init; }
    public string? LastSeenItemId { get; init; }
    public Guid? LastChatMessageId { get; init; }
    public Guid? LastEventId { get; init; }
}
