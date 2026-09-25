using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Credentials;

[Trait("Category", "Browser")]
public sealed class GameServerDetailsCredentialVisibilityTests
{
    public static TheoryData<string, bool> Principals => new()
    {
        { TestPrincipalProfiles.HeadAdmin, true },
        { TestPrincipalProfiles.GameServerWriterWithoutRcon, false },
    };

    [Theory]
    [MemberData(nameof(Principals))]
    public async Task Sftp_private_key_credentials_require_file_transport_read_access(
        string profile,
        bool expectCredentials)
    {
        var scenario = new CredentialsContentScenario(privateKeyAuthentication: true);

        await using var fixture = await BrowserFixture.CreateAsync(profile, scenario.ConfigureServices);

        var detailsUrl = new Uri(
            fixture.Host.BaseAddress,
            $"/GameServers/Details/{scenario.GameServerId}").ToString();
        var response = await fixture.Page.GotoAsync(detailsUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        Assert.NotNull(response);
        Assert.True(response.Ok, $"[{profile}] {detailsUrl} returned {response.Status}.");

        await Assertions.Expect(fixture.Page.GetByText(
                CredentialsContentScenario.SftpPrivateKey,
                new() { Exact = true }))
            .ToHaveCountAsync(expectCredentials ? 1 : 0);
        await Assertions.Expect(fixture.Page.GetByText(
                CredentialsContentScenario.SftpPrivateKeyPassphrase,
                new() { Exact = true }))
            .ToHaveCountAsync(expectCredentials ? 1 : 0);
    }
}
