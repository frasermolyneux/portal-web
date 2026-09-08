using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Credentials;

/// <summary>
/// Validates that the Credentials index page not only loads for every admin role (landing gate is
/// <c>GameServers.Admin.Read</c>) but that the sensitive credential content is filtered strictly per
/// role and per direct grant once the page renders. This is the "content is strictly protected once
/// loaded" half of the credentials access model.
/// </summary>
/// <remarks>
/// A shared server (<see cref="TestPrincipalProfiles.CredentialServerId"/>) exposes both RCON and SFTP
/// configuration. Each principal is expected to see:
/// <list type="bullet">
/// <item>SeniorAdmin / HeadAdmin(CoD4): the server row with both the RCON and file transport columns.</item>
/// <item>GameAdmin(CoD4): the server row with the RCON column only (file transport is HeadAdmin+).</item>
/// <item>Moderator(CoD4): an empty page — a Moderator role grants no credential visibility.</item>
/// <item>Moderator + direct file transport grant: the server row with file transport columns only.</item>
/// <item>Moderator + direct RCON grant: the server row with the RCON column only.</item>
/// </list>
/// Each principal is exercised against its own host because the repository mock is shaped per test.
/// </remarks>
[Trait("Category", "Browser")]
public sealed class CredentialsContentVisibilityTests
{
    public static TheoryData<string, bool, bool, bool> Principals => new()
    {
        // profile, expectServerRow, expectRconVisible, expectFileTransportVisible
        { TestPrincipalProfiles.SeniorAdmin, true, true, true },
        { TestPrincipalProfiles.HeadAdmin, true, true, true },
        { TestPrincipalProfiles.GameAdmin, true, true, false },
        { TestPrincipalProfiles.Moderator, false, false, false },
        { TestPrincipalProfiles.CredentialFileTransportReader, true, false, true },
        { TestPrincipalProfiles.CredentialRconReader, true, true, false },
    };

    [Theory]
    [MemberData(nameof(Principals))]
    public async Task Credentials_content_is_filtered_per_role_and_grant(
        string profile,
        bool expectServerRow,
        bool expectRconVisible,
        bool expectFileTransportVisible)
    {
        var scenario = new CredentialsContentScenario();

        await using var fixture = await BrowserFixture.CreateAsync(profile, scenario.ConfigureServices);

        var credentialsUrl = new Uri(fixture.Host.BaseAddress, "/Credentials").ToString();
        var response = await fixture.Page.GotoAsync(credentialsUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        Assert.NotNull(response);
        Assert.True(response.Ok, $"[{profile}] /Credentials returned {response.Status}.");

        var serverId = scenario.GameServerId;

        await Assertions.Expect(fixture.Page.Locator("table.w-100 tbody tr")).ToHaveCountAsync(expectServerRow ? 1 : 0);
        await Assertions.Expect(fixture.Page.GetByRole(AriaRole.Columnheader, new() { Name = "RCON Password", Exact = true }))
            .ToHaveCountAsync(expectRconVisible ? 1 : 0);
        await Assertions.Expect(fixture.Page.GetByRole(AriaRole.Columnheader, new() { Name = "File Transport Username", Exact = true }))
            .ToHaveCountAsync(expectFileTransportVisible ? 1 : 0);
        await Assertions.Expect(fixture.Page.Locator($"#rconPassword-{serverId}")).ToHaveCountAsync(expectRconVisible ? 1 : 0);
        await Assertions.Expect(fixture.Page.Locator($"#ftpUsername-{serverId}")).ToHaveCountAsync(expectFileTransportVisible ? 1 : 0);
        await Assertions.Expect(fixture.Page.Locator($"#ftpPassword-{serverId}")).ToHaveCountAsync(expectFileTransportVisible ? 1 : 0);
    }
}
