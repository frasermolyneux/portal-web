using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class ManageMapsViewModel(GameServerDto gameServer)
{
    public GameServerDto GameServer { get; private set; } = gameServer;
    public GameServerLiveStatusDto? LiveStatus { get; set; }
    public List<MapDto> Maps { get; set; } = [];
    public List<ServerMapDto> ServerMaps { get; set; } = [];
    public PublicServerRotationDto? EffectiveRotation { get; set; }
    public List<MapRotationServerAssignmentDto> RotationAssignments { get; set; } = [];
    public Dictionary<Guid, MapRotationDto> Rotations { get; set; } = [];

    /// <summary>
    /// The currently active portal-managed rotation assignment (if any).
    /// </summary>
    public MapRotationServerAssignmentDto? ActiveAssignment => RotationAssignments
        .FirstOrDefault(a => a.MapRotationServerAssignmentId == EffectiveRotation?.MapRotationServerAssignmentId);

    /// <summary>
    /// The rotation DTO for the active assignment (if any).
    /// </summary>
    public MapRotationDto? ActiveRotation => ActiveAssignment != null && Rotations.TryGetValue(ActiveAssignment.MapRotationId, out var r) ? r : null;

    /// <summary>
    /// All map names in the active rotation (portal-managed), used for "In Rotation" checks.
    /// </summary>
    public HashSet<string> ActiveRotationMapNames => EffectiveRotation?.Maps
        .Select(map => map.MapName).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
}
