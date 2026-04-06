namespace XtremeIdiots.Portal.Web.Services;

public record AgentServerStatus
{
    public DateTime? LastEventReceived { get; init; }
    public int EventsLastHour { get; init; }
    public int PlayersConnectedLastHour { get; init; }
    public int ChatMessagesLastHour { get; init; }
    public DateTime? LastMapChange { get; init; }
    public string? LastMapName { get; init; }
    public int BansDetectedLast24h { get; init; }
    public int ModerationTriggersLast24h { get; init; }
    public bool IsAgentActive { get; init; }
}

public record AgentServerSummary
{
    public Guid ServerId { get; init; }
    public string? ServerTitle { get; init; }
    public string? GameType { get; init; }
    public DateTime? LastEventReceived { get; init; }
    public int EventsLastHour { get; init; }
    public int PlayerCount { get; init; }
    public string? CurrentMap { get; init; }
    public bool IsAgentActive { get; init; }
}

public interface IAgentTelemetryService
{
    Task<AgentServerStatus> GetServerStatusAsync(Guid serverId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentServerSummary>> GetAllServersStatusAsync(CancellationToken ct = default);
}
