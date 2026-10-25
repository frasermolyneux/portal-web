using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class AssignmentStatusViewModel
{
    public required MapRotationServerAssignmentDto Assignment { get; set; }
    public required MapRotationDto Rotation { get; set; }
    public MapRotationAssignmentGroup Group => Rotation.AssignmentGroup(Assignment);
    public GameServerDto? GameServer { get; set; }
    public string GameServerDisplayName => GameServer?.Title ?? $"Unknown Server ({Assignment.GameServerId})";
    public List<MapRotationAssignmentOperationDto> Operations { get; set; } = [];
    public bool ActivityNeedsAttention => Group.NeedsAttention || (Group.IsCombined && Operations
        .GroupBy(o => o.MapRotationServerAssignmentId)
        .Any(history => history.OrderByDescending(o => o.StartedAt).First().Status == AssignmentOperationStatus.Failed));
    public bool IsStale => Assignment.DeployedVersion.HasValue && Assignment.DeployedVersion < Rotation.Version;
    public bool IsActivationStale => Assignment.ActivatedVersion.HasValue && Assignment.ActivatedVersion < Rotation.Version;
}
