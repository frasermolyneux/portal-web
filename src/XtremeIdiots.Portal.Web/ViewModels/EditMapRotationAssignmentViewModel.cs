using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class EditMapRotationAssignmentViewModel
{
    public Guid MapRotationServerAssignmentId { get; set; }

    public Guid MapRotationId { get; set; }

    public Guid GameServerId { get; set; }

    public string? GameServerTitle { get; set; }

    [DisplayName("Config File")]
    public string? ConfigFilePath { get; set; }

    [DisplayName("Config Variable")]
    public string? ConfigVariableName { get; set; }

    [DisplayName("Min Players")]
    public int? PlayerCountMin { get; set; }

    [DisplayName("Max Players")]
    public int? PlayerCountMax { get; set; }

    public bool CanBrowseFtp { get; set; }

    public bool FtpEnabled { get; set; }
}
