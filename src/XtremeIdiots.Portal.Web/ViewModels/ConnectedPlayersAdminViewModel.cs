using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class ConnectedPlayersAdminViewModel
{
    public IList<ConnectedPlayerAdminItemViewModel> ConnectedPlayers { get; init; } = [];

    public GameType? FilterGameType { get; init; }

    public bool? FilterIsActive { get; init; }

    public int CurrentPage { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public bool IsSeniorAdmin { get; init; }

    public CreateConnectedPlayerLinkInput ManualLink { get; init; } = new();
}

public sealed class ConnectedPlayerAdminItemViewModel
{
    public Guid ConnectedPlayerProfileId { get; init; }

    public Guid PlayerId { get; init; }

    public Guid UserProfileId { get; init; }

    public GameType GameType { get; init; }

    public string Username { get; init; } = string.Empty;

    public string LinkMethod { get; init; } = string.Empty;

    public DateTime LinkedAtUtc { get; init; }

    public DateTime? UnlinkedAtUtc { get; init; }

    public bool IsActive { get; init; }
}

public sealed class CreateConnectedPlayerLinkInput
{
    public Guid PlayerId { get; init; }

    public Guid UserProfileId { get; init; }
}
