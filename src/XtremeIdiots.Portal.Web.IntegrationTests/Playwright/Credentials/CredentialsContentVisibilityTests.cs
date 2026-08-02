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
        Assert.True(response!.Ok, $"[{profile}] /Credentials returned {response.Status}.");

        var serverId = scenario.GameServerId;

        var rowCount = await fixture.Page.Locator("table.w-100 tbody tr").CountAsync();
        var rconHeaderCount = await fixture.Page
            .GetByRole(AriaRole.Columnheader, new PageGetByRoleOptions { Name = "RCON Password", Exact = true })
            .CountAsync();
        var fileTransportHeaderCount = await fixture.Page
            .GetByRole(AriaRole.Columnheader, new PageGetByRoleOptions { Name = "File Transport Username", Exact = true })
            .CountAsync();
        var rconValueCount = await fixture.Page.Locator($"#rconPassword-{serverId}").CountAsync();
        var ftpUsernameValueCount = await fixture.Page.Locator($"#ftpUsername-{serverId}").CountAsync();
        var ftpPasswordValueCount = await fixture.Page.Locator($"#ftpPassword-{serverId}").CountAsync();

        if (expectServerRow)
        {
            Assert.True(rowCount == 1, $"[{profile}] expected exactly one credential row, saw {rowCount}.");
        }
        else
        {
            Assert.True(rowCount == 0, $"[{profile}] expected an empty credentials table, saw {rowCount} row(s).");
        }

        if (expectRconVisible)
        {
            Assert.True(rconHeaderCount == 1, $"[{profile}] expected the RCON column header, saw {rconHeaderCount}.");
            Assert.True(rconValueCount == 1, $"[{profile}] expected the RCON password value, saw {rconValueCount}.");
        }
        else
        {
            Assert.True(rconHeaderCount == 0, $"[{profile}] did not expect the RCON column header, saw {rconHeaderCount}.");
            Assert.True(rconValueCount == 0, $"[{profile}] did not expect an RCON password value, saw {rconValueCount}.");
        }

        if (expectFileTransportVisible)
        {
            Assert.True(fileTransportHeaderCount == 1, $"[{profile}] expected the file transport column header, saw {fileTransportHeaderCount}.");
            Assert.True(ftpUsernameValueCount == 1, $"[{profile}] expected the file transport username value, saw {ftpUsernameValueCount}.");
            Assert.True(ftpPasswordValueCount == 1, $"[{profile}] expected the file transport password value, saw {ftpPasswordValueCount}.");
        }
        else
        {
            Assert.True(fileTransportHeaderCount == 0, $"[{profile}] did not expect the file transport column header, saw {fileTransportHeaderCount}.");
            Assert.True(ftpUsernameValueCount == 0, $"[{profile}] did not expect a file transport username value, saw {ftpUsernameValueCount}.");
            Assert.True(ftpPasswordValueCount == 0, $"[{profile}] did not expect a file transport password value, saw {ftpPasswordValueCount}.");
        }
    }
}
