using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels.Analytics;

/// <summary>
/// A selectable game server option for analytics command-centre dropdowns.
/// </summary>
public sealed record AnalyticsServerOption(Guid GameServerId, string Title, GameType GameType);

/// <summary>
/// View model for the Global analytics command centre. The page is driven entirely by
/// client-side fetches against the analytics API, so no server-side data is required.
/// </summary>
public sealed class AnalyticsGlobalViewModel
{
}

/// <summary>
/// View model for the Game analytics command centre.
/// </summary>
public sealed class AnalyticsGameViewModel
{
    public IReadOnlyList<GameType> Games { get; init; } = [];

    public GameType SelectedGame { get; init; } = GameType.Unknown;
}

/// <summary>
/// View model for the Server analytics command centre.
/// </summary>
public sealed class AnalyticsServerViewModel
{
    public IReadOnlyList<AnalyticsServerOption> Servers { get; init; } = [];

    public Guid? SelectedServerId { get; init; }
}

/// <summary>
/// View model for the Player analytics command centre (aggregate; no entity selector).
/// </summary>
public sealed class AnalyticsPlayerViewModel
{
}

/// <summary>
/// View model for the Maps analytics command centre.
/// </summary>
public sealed class AnalyticsMapsViewModel
{
    public IReadOnlyList<GameType> Games { get; init; } = [];

    public IReadOnlyList<AnalyticsServerOption> Servers { get; init; } = [];
}
