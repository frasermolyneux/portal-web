using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Navigation;

/// <summary>
/// Verifies that every read-only side-navigation entry is rendered for exactly the roles that should
/// see it. The navigation lives in the shared layout, so a single neutral page (<c>/ChangeLog</c>,
/// which renders for every role including anonymous) exercises the whole menu. Assertions are on DOM
/// presence because the policy tag helper removes unauthorized entries from the output.
/// </summary>
[Collection(PortalPlaywrightTestGroup.Name)]
public sealed class NavigationVisibilityTests(PortalPlaywrightServerFixture fixture)
{
    private readonly PortalPlaywrightServerFixture fixture = fixture;

    [Fact]
    public async Task Navigation_entries_are_rendered_for_exactly_the_expected_roles()
    {
        var neutralPage = new Uri(fixture.BaseAddress, "/ChangeLog").ToString();
        var mismatches = new List<string>();

        foreach (var role in TestRoles.Baseline)
        {
            await using var session = await fixture.CreateRoleSessionAsync(role);

            var response = await session.Page.GotoAsync(neutralPage, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            Assert.NotNull(response);
            Assert.True(response!.Ok, $"[{role}] navigation harness page returned {response.Status}.");

            foreach (var item in NavigationItemCatalog.Items)
            {
                var count = await session.Page.Locator(item.Selector).CountAsync();
                var rendered = count > 0;
                var expected = NavigationItemCatalog.IsVisible(item, role);

                if (rendered != expected)
                {
                    var note = item.Note is null ? string.Empty : $" (note: {item.Note})";
                    mismatches.Add($"{item.Name} [{role}]: expected {(expected ? "visible" : "hidden")}, was {(rendered ? "visible" : "hidden")} (matched {count} element(s)).{note}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, "Navigation visibility mismatches:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }
}
