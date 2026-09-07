namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

internal static class ManifestSnapshotDiff
{
    public static string Compare(IReadOnlyList<string> approved, IReadOnlyList<string> actual)
    {
        if (approved.SequenceEqual(actual, StringComparer.Ordinal))
            return string.Empty;

        var removed = approved.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var added = actual.Except(approved, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var changes = removed.Select(line => $"- {line}").Concat(added.Select(line => $"+ {line}")).ToArray();
        var details = changes.Length == 0
            ? "Action ordering or duplicate counts changed."
            : string.Join(Environment.NewLine, changes);

        return $"Action manifest changed: approved={approved.Count}, actual={actual.Count}, removed={removed.Length}, added={added.Length}.{Environment.NewLine}{details}";
    }
}
