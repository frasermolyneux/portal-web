namespace XtremeIdiots.Portal.Web.Services;

/// <summary>
/// Operational health of a game server agent, independent of game-event activity.
/// </summary>
public enum AgentActivityStatus
{
    /// <summary>Agent loop reported a heartbeat within the last 5 minutes.</summary>
    Active,

    /// <summary>Legacy activity state retained for serialized compatibility.</summary>
    Idle,

    /// <summary>A previously observed operational heartbeat is overdue.</summary>
    Offline,

    /// <summary>No operational heartbeat is available; game events do not establish health.</summary>
    Unknown
}

public record AgentServerStatus
{
    public DateTime? LastHeartbeatReceived { get; init; }
    public string? SourceMode { get; init; }
    public DateTime? LastEventReceived { get; init; }
    public int EventsLastHour { get; init; }
    public int PlayersConnectedLastHour { get; init; }
    public int ChatMessagesLastHour { get; init; }
    public DateTime? LastMapChange { get; init; }
    public string? LastMapName { get; init; }
    public int BansDetectedLast24h { get; init; }
    public int ModerationTriggersLast24h { get; init; }
    public bool IsAgentActive { get; init; }
    public AgentActivityStatus ActivityStatus { get; init; } = AgentActivityStatus.Unknown;
}

public record AgentServerSummary
{
    public OptionalDataState TelemetryState { get; init; } = OptionalDataState.Available;
    public Guid ServerId { get; init; }
    public string? ServerTitle { get; init; }
    public string? GameType { get; init; }
    public DateTime? LastHeartbeatReceived { get; init; }
    public string? SourceMode { get; init; }
    public DateTime? LastEventReceived { get; init; }
    public int EventsLastHour { get; init; }
    public int PlayerCount { get; init; }
    public string? CurrentMap { get; init; }
    public bool IsAgentActive { get; init; }
    public AgentActivityStatus ActivityStatus { get; init; } = AgentActivityStatus.Unknown;
}

public interface IAgentTelemetryService
{
    Task<AgentServerStatus> GetServerStatusAsync(Guid serverId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentServerSummary>> GetAllServersStatusAsync(CancellationToken ct = default);
}
