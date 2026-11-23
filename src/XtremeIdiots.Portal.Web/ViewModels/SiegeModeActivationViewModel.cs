namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class SiegeModeActivationViewModel
{
    public required Guid GameServerId { get; init; }
    public required string ServerName { get; init; }
    public required int DurationMinutes { get; init; }
    public required Guid OperationId { get; init; }
    public required string Actor { get; init; }
}
