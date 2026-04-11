using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class CreateMapRotationAssignmentViewModel
{
    public Guid MapRotationId { get; set; }

    [Required]
    [DisplayName("Game Server")]
    public Guid GameServerId { get; set; }

    [DisplayName("Config File")]
    public string? ConfigFilePath { get; set; }

    [DisplayName("Config Variable")]
    public string? ConfigVariableName { get; set; }

    [DisplayName("Min Players")]
    public int? PlayerCountMin { get; set; }

    [DisplayName("Max Players")]
    public int? PlayerCountMax { get; set; }

    public List<GameServerDto> AvailableServers { get; set; } = [];

    public bool CanBrowseFtp { get; set; }
}
