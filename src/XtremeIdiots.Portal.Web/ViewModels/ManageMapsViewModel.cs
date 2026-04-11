using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class ManageMapsViewModel(GameServerDto gameServer)
{
    public GameServerDto GameServer { get; private set; } = gameServer;
    public List<MapDto> Maps { get; set; } = [];
    public List<ServerMapDto> ServerMaps { get; set; } = [];
    public List<RconMapDto> RconMaps { get; set; } = [];
    public List<MapRotationServerAssignmentDto> RotationAssignments { get; set; } = [];
    public Dictionary<Guid, MapRotationDto> Rotations { get; set; } = [];

    /// <summary>
    /// The currently active portal-managed rotation assignment (if any).
    /// </summary>
    public MapRotationServerAssignmentDto? ActiveAssignment => RotationAssignments
        .FirstOrDefault(a => a.ActivationState == ActivationState.Active);

    /// <summary>
    /// The rotation DTO for the active assignment (if any).
    /// </summary>
    public MapRotationDto? ActiveRotation => ActiveAssignment != null && Rotations.TryGetValue(ActiveAssignment.MapRotationId, out var r) ? r : null;

    /// <summary>
    /// All map names in the active rotation (portal-managed), used for "In Rotation" checks.
    /// </summary>
    public HashSet<string> ActiveRotationMapNames => ActiveRotation?.MapRotationMaps?
        .Select(m => Maps.FirstOrDefault(map => map.MapId == m.MapId)?.MapName)
        .Where(n => n != null)
        .Select(n => n!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];
}