using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class MapRotationDtoExtensions
{
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
