using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class CreateMapRotationAssignmentViewModel : IValidatableObject
{
    public Guid MapRotationId { get; set; }

    [Required]
    [DisplayName("Game Server")]
    public Guid GameServerId { get; set; }

    [DisplayName("Config File")]
    public string? ConfigFilePath { get; set; }

    [DisplayName("Config Variable")]
    public string? ConfigVariableName { get; set; }

    [DisplayName("Also update AACP map picker")]
    public bool AacpEnabled { get; set; }

    [DisplayName("AACP config file")]
    public string? AacpConfigFilePath { get; set; }

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

    public List<GameServerDto> AvailableServers { get; set; } = [];

    public bool CanBrowseServerFiles { get; set; }
}
