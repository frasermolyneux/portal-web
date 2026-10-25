using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using GameType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameType;
using RepoFileTransportType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class EditMapRotationAssignmentViewModel : IValidatableObject
{
    public Guid MapRotationServerAssignmentId { get; set; }

    public Guid MapRotationId { get; set; }

    public Guid GameServerId { get; set; }

    public string? GameServerTitle { get; set; }

    [DisplayName("Config File")]
    public string? ConfigFilePath { get; set; }

    [DisplayName("Config Variable")]
    public string? ConfigVariableName { get; set; }

    [DisplayName("Also update AACP map picker")]
    public bool AacpEnabled { get; set; }

    [DisplayName("AACP config file")]
    public string? AacpConfigFilePath { get; set; }

    public bool CanConfigureAacp { get; set; }
    public bool AacpRequiresDeactivation { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AacpEnabled && string.IsNullOrWhiteSpace(AacpConfigFilePath))
            yield return new ValidationResult("Choose the AACP config file.", [nameof(AacpConfigFilePath)]);
        if (AacpEnabled && !string.Equals(ConfigVariableName, "sv_maprotation", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("AACP map picker can only be linked to the main sv_maprotation assignment.", [nameof(ConfigVariableName)]);
    }

    [DisplayName("Min Players")]
    public int? PlayerCountMin { get; set; }

    [DisplayName("Max Players")]
    public int? PlayerCountMax { get; set; }

    public bool CanBrowseServerFiles { get; set; }
    public RepoFileTransportType FileTransportType { get; set; } = RepoFileTransportType.Unknown;
}
