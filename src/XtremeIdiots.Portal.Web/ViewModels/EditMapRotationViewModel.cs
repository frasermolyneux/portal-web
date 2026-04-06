using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class EditMapRotationViewModel
{
    public Guid MapRotationId { get; set; }

    [Required]
    [DisplayName("Title")]
    public required string Title { get; set; }

    [DisplayName("Description")]
    public string? Description { get; set; }

    [Required]
    [DisplayName("Game Mode")]
    public required string GameMode { get; set; }

    public GameType GameType { get; set; }
    public int Version { get; set; }
    public List<Guid> MapIds { get; set; } = [];
}
