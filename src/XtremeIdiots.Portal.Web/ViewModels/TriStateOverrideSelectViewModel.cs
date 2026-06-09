namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class TriStateOverrideSelectViewModel
{
    public required string FieldName { get; init; }

    public required string Label { get; init; }

    public required bool? Value { get; init; }

    public required string InheritLabel { get; init; }

    public string? HelpText { get; init; }
}
