using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class CreateMapRotationViewModel
{
    [Required]
    [DisplayName("Game")]
    public GameType GameType { get; set; }

    [Required]
    [DisplayName("Title")]
    public required string Title { get; set; }

    [DisplayName("Description")]
    public string? Description { get; set; }

    [Required]
    [DisplayName("Game Mode")]
    public required string GameMode { get; set; }

    public List<Guid> MapIds { get; set; } = [];
}
