using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class PlayersIndexViewModel
{
    public IReadOnlyList<GameType> AllowedGameTypes { get; init; } = [];

    public bool CanViewAllGames { get; init; }

    public GameType? SelectedGameType { get; init; }

    public List<TagDto> Tags { get; init; } = [];
}
