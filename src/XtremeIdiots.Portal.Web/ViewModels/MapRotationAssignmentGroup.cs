using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class MapRotationAssignmentGroup(IReadOnlyList<MapRotationServerAssignmentDto> targets, bool isAmbiguous = false)
{
    public bool IsAmbiguous { get; } = isAmbiguous;
    public IReadOnlyList<MapRotationServerAssignmentDto> Targets { get; } = targets;
    public MapRotationServerAssignmentDto Primary => Targets[0];
    public bool IsCombined => Targets.Count > 1;
    public string Label => IsCombined ? "Main rotation + AACP map picker" : Primary.SlotLabel();
    public bool IsBusy => Targets.Any(a => a.DeploymentState is DeploymentState.Syncing or DeploymentState.Removing
        || a.ActivationState is ActivationState.Activating or ActivationState.Deactivating);
    public bool IsInactive => Targets.All(a => a.ActivationState == ActivationState.Inactive);
    public bool NeedsAttention => Targets.Any(a => !string.IsNullOrEmpty(a.LastError)
        || a.DeploymentState == DeploymentState.Failed || a.ActivationState == ActivationState.Failed);
}
