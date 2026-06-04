namespace XtremeIdiots.Portal.Web.Models.ServerFeed;

public class ServerFeedItemDto
{
    public required string ItemId { get; init; }
    public required string SourceType { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required string DisplayText { get; init; }
    public string? Username { get; init; }
    public Guid? PlayerId { get; init; }
    public string? EventType { get; init; }
    public string? RawEventData { get; init; }
    public bool Locked { get; init; }
}
