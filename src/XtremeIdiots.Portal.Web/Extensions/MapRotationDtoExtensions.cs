using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class MapRotationDtoExtensions
{
    public static bool IsMainRotation(this MapRotationServerAssignmentDto assignment)
    {
        return string.Equals(assignment.ConfigVariableName, "sv_maprotation", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAacpMapPicker(this MapRotationServerAssignmentDto assignment)
    {
        return string.Equals(assignment.ConfigVariableName, "scr_aacp_maps_1", StringComparison.OrdinalIgnoreCase);
    }

    public static List<MapRotationAssignmentGroup> AssignmentGroups(this IEnumerable<MapRotationServerAssignmentDto> assignments)
    {
        var rows = assignments.ToList();
        var groups = new List<MapRotationAssignmentGroup>();
        var grouped = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!grouped.Add(row.MapRotationServerAssignmentId))
                continue;

            var candidates = rows.Where(a => a.MapRotationId == row.MapRotationId && a.GameServerId == row.GameServerId
                && a.DeploymentState != DeploymentState.Removed && (a.IsMainRotation() || a.IsAacpMapPicker())).ToList();
            // Ambiguous legacy slots and removed history must remain independently inspectable.
            if (row.DeploymentState != DeploymentState.Removed && (row.IsMainRotation() || row.IsAacpMapPicker())
                && candidates.Count(a => a.IsMainRotation()) == 1 && candidates.Count(a => a.IsAacpMapPicker()) == 1)
            {
                var pair = candidates.OrderByDescending(a => a.IsMainRotation()).ToList();
                foreach (var target in pair)
                    grouped.Add(target.MapRotationServerAssignmentId);
                groups.Add(new(pair));
            }
            else
                groups.Add(new([row], row.DeploymentState != DeploymentState.Removed
                    && (row.IsMainRotation() || row.IsAacpMapPicker())
                    && (candidates.Count(a => a.IsMainRotation()) > 1 || candidates.Count(a => a.IsAacpMapPicker()) > 1)));
        }
        return groups;
    }

    public static MapRotationAssignmentGroup AssignmentGroup(this MapRotationDto rotation, MapRotationServerAssignmentDto assignment)
    {
        return rotation.ServerAssignments?.AssignmentGroups().FirstOrDefault(g =>
            g.Targets.Any(a => a.MapRotationServerAssignmentId == assignment.MapRotationServerAssignmentId))
        ?? new MapRotationAssignmentGroup([assignment]);
    }

    public static string SlotLabel(this MapRotationServerAssignmentDto assignment)
    {
        return assignment.ConfigVariableName?.ToLowerInvariant() switch
        {
            "sv_maprotation" => "Main rotation",
            "scr_aacp_maps_1" => "Map picker",
            "scr_small_rotation" or "sv_maprotation_low" => "Small rotation",
            "scr_med_rotation" or "sv_maprotation_medium" => "Medium rotation",
            "scr_large_rotation" or "sv_maprotation_high" => "Large rotation",
            null or "" => "Files only",
            _ => assignment.ConfigVariableName
        };
    }

    public static string ActionLabel(this MapRotationAssignmentOperationDto operation)
    {
        var isApply = operation.DurableFunctionInstanceId?.StartsWith("maprot-apply-", StringComparison.Ordinal) == true;
        return operation.OperationType switch
        {
            AssignmentOperationType.Sync when isApply => "Apply rotation: files",
            AssignmentOperationType.Activate when isApply => "Apply rotation: configuration",
            AssignmentOperationType.Sync => "Repair files",
            AssignmentOperationType.Activate => "Reapply configuration",
            AssignmentOperationType.Deactivate => "Deactivate",
            AssignmentOperationType.Remove => "Unassign",
            AssignmentOperationType.Verify => "Verify files",
            _ => operation.OperationType.ToString()
        };
    }
}
