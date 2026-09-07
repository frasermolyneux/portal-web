namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

[Trait("Category", "HttpIntegration")]
public sealed class ManifestSnapshotDiffTests
{
    [Fact]
    public void MatchingManifest_HasNoDifference()
    {
        Assert.Empty(ManifestSnapshotDiff.Compare(["Action|BrowserPage"], ["Action|BrowserPage"]));
    }

    [Fact]
    public void ChangedManifest_DescribesRemovedAndAddedActions()
    {
        var difference = ManifestSnapshotDiff.Compare(
            ["Removed|BrowserPage", "Shared|HttpEndpoint"],
            ["Added|StateChange", "Shared|HttpEndpoint"]);

        Assert.Contains("approved=2, actual=2, removed=1, added=1", difference, StringComparison.Ordinal);
        Assert.Contains("- Removed|BrowserPage", difference, StringComparison.Ordinal);
        Assert.Contains("+ Added|StateChange", difference, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared|HttpEndpoint", difference, StringComparison.Ordinal);
    }

    [Fact]
    public void ReclassifiedAction_ShowsBothClassifications()
    {
        var difference = ManifestSnapshotDiff.Compare(["Action|BrowserPage"], ["Action|HttpEndpoint"]);

        Assert.Contains("- Action|BrowserPage", difference, StringComparison.Ordinal);
        Assert.Contains("+ Action|HttpEndpoint", difference, StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderedManifest_ReportsOrderingChange()
    {
        Assert.Contains("ordering or duplicate counts changed",
            ManifestSnapshotDiff.Compare(["First", "Second"], ["Second", "First"]), StringComparison.Ordinal);
    }
}
