using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class AssignmentStatusViewModel
{
    public required MapRotationServerAssignmentDto Assignment { get; set; }
    public required MapRotationDto Rotation { get; set; }
    public GameServerDto? GameServer { get; set; }
    public string GameServerDisplayName => GameServer?.Title ?? $"Unknown Server ({Assignment.GameServerId})";
    public List<MapDto> Maps { get; set; } = [];
    public List<MapRotationAssignmentOperationDto> Operations { get; set; } = [];
    public bool IsStale => Assignment.DeployedVersion.HasValue && Assignment.DeployedVersion < Rotation.Version;
    public bool IsActivationStale => Assignment.ActivatedVersion.HasValue && Assignment.ActivatedVersion < Rotation.Version;
}
