namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class RequiredTagOptionViewModel
{
    public required string Name { get; init; }

    public string DisplayName { get; init; } = string.Empty;
}

public sealed class RequiredTagsSelectorViewModel
{
    public required string FieldName { get; init; }

    public required string Label { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<RequiredTagOptionViewModel> Options { get; init; } = [];

    public string? HelpText { get; init; }

    public string Placeholder { get; init; } = "Select one or more tags";

    public string? DataField { get; init; }

    public string? LabelDataField { get; init; }

    public string? SelectDataField { get; init; }

    public string? HelpDataField { get; init; }
}

public static class RequiredTagsSelection
{
    public static string[] SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            :
            [
                .. value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
    }
}
