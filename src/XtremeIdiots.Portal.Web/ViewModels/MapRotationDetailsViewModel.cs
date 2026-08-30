using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class MapRotationDetailsViewModel
{
    public required MapRotationDto Rotation { get; set; }
    public List<MapDto> Maps { get; set; } = [];
    public Dictionary<Guid, List<MapRotationAssignmentOperationDto>> AssignmentOperations { get; set; } = [];

    /// <summary>
    /// Whether the current user is authorized to deploy to at least one compatible server.
    /// Controls visibility of the "Assign to Server" button.
    /// </summary>
    public bool CanAssignServer { get; set; }

    /// <summary>
    /// Server IDs the current user is authorized to deploy to.
    /// Per-assignment deploy controls are shown only for servers in this set.
    /// </summary>
    public HashSet<Guid> AuthorizedServerIds { get; set; } = [];
}
