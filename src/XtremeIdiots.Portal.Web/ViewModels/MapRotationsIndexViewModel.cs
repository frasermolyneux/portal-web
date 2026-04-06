using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class MapRotationsIndexViewModel
{
    public List<MapRotationDto> MapRotations { get; set; } = [];
    public GameType? SelectedGameType { get; set; }
}
