namespace XtremeIdiots.Portal.Web.Models.ServerFeed;

public class ServerFeedSourceAuthorizationDto
{
    public bool ChatAllowed { get; init; }
    public bool EventsAllowed { get; init; }
}

public class ServerFeedDiagnosticsDto
{
    public int ChatCount { get; init; }
    public int EventCount { get; init; }
    public bool OverrunDetected { get; init; }
}

public class ServerFeedResponseDto
{
    public required IReadOnlyList<ServerFeedItemDto> Items { get; init; }
    public required ServerFeedCursorDto Cursor { get; init; }
    public required ServerFeedSourceAuthorizationDto SourceAuthorization { get; init; }
    public required ServerFeedDiagnosticsDto Diagnostics { get; init; }
    public required string ServerTimeUtc { get; init; }
}
