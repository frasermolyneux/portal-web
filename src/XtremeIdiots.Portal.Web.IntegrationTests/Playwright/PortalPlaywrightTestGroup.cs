namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>
/// Navigation tests share one Kestrel host. The assembly, not this collection, owns the browser.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PortalPlaywrightTestGroup : ICollectionFixture<PortalPlaywrightServerFixture>
{
    public const string Name = "PortalPlaywright";
}
