namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>
/// xUnit collection that shares a single <see cref="PortalPlaywrightServerFixture"/> (one Kestrel
/// host and one browser) across all navigation and UI tests, so the suite scales without the
/// per-test host/browser cost.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PortalPlaywrightTestGroup : ICollectionFixture<PortalPlaywrightServerFixture>
{
    public const string Name = "PortalPlaywright";
}
