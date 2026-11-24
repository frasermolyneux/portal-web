using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Trait("Category", "Browser")]
public sealed class VpnRuleEditorBrowserTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServerOverride_CanReplaceOrInheritEntireCondition(bool replace)
    {
        var scenario = new FileTransportScenario();
        var global = Configuration(/*lang=json,strict*/ """
            {"schemaVersion":2,"enabled":false,"rules":[{"id":"global-vpn","condition":{"signal":"ProxyCheckIsVpn","operator":"Equal","expectedValue":"true"},"action":"Observation"}]}
            """);
        var server = Configuration(/*lang=json,strict*/ """
            {"schemaVersion":2,"rules":[{"id":"local","condition":{"signal":"MaxMindIsAnonymous","operator":"Equal","expectedValue":"true"},"action":"Observation"}],
             "ruleOverrides":[{"id":"global-vpn","condition":{"groupOperator":"Or","conditions":[{"signal":"MaxMindIsTorExitNode","operator":"Equal","expectedValue":"true"}]},"enabled":false}]}
            """);
        Mock.Get(scenario.RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([global]))));
        Mock.Get(scenario.RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([scenario.SftpConfiguration, server]))));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            scenario.ConfigureServices(services);
            services.RemoveAll<IGameServerSettingsService>();
            services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        });
        var page = browser.Page;
        var path = $"/GameServers/Edit/{scenario.GameServerId}";
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, path).AbsoluteUri);
        await page.Locator("#vpn-protection-tab-btn").ClickAsync();
        var row = page.Locator("#server-vpn-overrides > [data-siege-rule-row]").First;
        var toggle = row.Locator("[data-rule-field='UseConditionOverride']");
        await Assertions.Expect(toggle).ToBeCheckedAsync();
        if (replace)
        {
            var root = row.Locator(":scope > [data-siege-condition-node]");
            await root.Locator(":scope > .row [data-action='add-siege-vpn-condition']").ClickAsync();
            var children = root.Locator(":scope > [data-condition-group] > [data-siege-children] > [data-siege-condition-node]");
            await children.Nth(1).Locator("[data-condition-field='Signal']").SelectOptionAsync("ProxyCheckIsProxy");
        }
        else
        {
            await toggle.UncheckAsync();
            await Assertions.Expect(row.Locator("[data-siege-condition-node]").First).ToBeHiddenAsync();
        }

        await page.Locator("#server-vpn-rules-add").ClickAsync();
        var localRows = page.Locator("#server-vpn-rules > [data-siege-rule-row]");
        await Assertions.Expect(localRows).ToHaveCountAsync(2);
        await localRows.Nth(1).Locator("[data-rule-field='Id']").FillAsync("new-local");

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("game-server-save").ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(302, response.Status);
        var command = Assert.Single(scenario.UpsertedConfigurations, entry => entry.Namespace == VpnProtectionSettingsConstants.Namespace);
        using var saved = JsonDocument.Parse(command.Configuration);
        var condition = saved.RootElement.GetProperty("ruleOverrides")[0].GetProperty("condition");
        if (replace)
        {
            Assert.Equal("Or", condition.GetProperty("groupOperator").GetString());
            Assert.Equal("ProxyCheckIsProxy", condition.GetProperty("conditions")[1].GetProperty("signal").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, condition.ValueKind);
        }

        Assert.False(saved.RootElement.GetProperty("ruleOverrides")[0].GetProperty("enabled").GetBoolean());
        Assert.Equal("MaxMindIsAnonymous", saved.RootElement.GetProperty("rules")[0].GetProperty("condition").GetProperty("signal").GetString());
        Assert.Equal("new-local", saved.RootElement.GetProperty("rules")[1].GetProperty("id").GetString());
        browser.AssertNoBrowserErrors();
    }

    private static ConfigurationDto Configuration(string json)
        => JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = VpnProtectionSettingsConstants.Namespace,
            Configuration = json,
            LastModifiedUtc = DateTime.UtcNow
        }))!;
}
