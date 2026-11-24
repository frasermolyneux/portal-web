using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GlobalSettings;

[Trait("Category", "Browser")]
public sealed class SiegeModeSettingsBrowserTests
{
    [Fact]
    public async Task Editor_AddsEditsAndRemovesNestedGroups_AndRoundTrips()
    {
        var scenario = new SettingsScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);

        var rules = page.Locator("#global-siege-rules > [data-siege-rule-row]");
        await Assertions.Expect(rules).ToHaveCountAsync(2);
        await rules.Nth(1).Locator("[data-action='remove-siege-rule']").ClickAsync();
        await rules.Nth(0).Locator("[data-action='remove-siege-rule']").ClickAsync();
        await page.Locator("#add-global-siege-rule").ClickAsync();
        await Assertions.Expect(rules).ToHaveCountAsync(1);
        var rule = rules.Nth(0);
        await rule.Locator("[data-rule-field='Id']").FillAsync("nested-test");
        var root = rule.Locator(":scope > [data-siege-condition-mount] > [data-siege-condition-node]");
        await Assertions.Expect(root).ToHaveCountAsync(1);
        await root.Locator(":scope > .row [data-action='add-siege-group']").ClickAsync();
        await Assertions.Expect(root).ToHaveCountAsync(1);
        var children = root.Locator(":scope > [data-condition-group] > [data-siege-children] > [data-siege-condition-node]");
        await Assertions.Expect(children).ToHaveCountAsync(2);
        var nested = children.Nth(1);
        var nestedSelector = nested.Locator(":scope > .row [data-condition-kind-selector]");
        await nestedSelector.SelectOptionAsync("player");
        await nestedSelector.SelectOptionAsync("group");
        var nestedChildren = nested.Locator(":scope > [data-condition-group] > [data-siege-children] > [data-siege-condition-node]");
        var nestedChild = nestedChildren.Nth(0);
        var childSelector = nestedChild.Locator(":scope > .row [data-condition-kind-selector]");
        await Assertions.Expect(childSelector).ToBeEnabledAsync();
        await childSelector.SelectOptionAsync("vpn");
        await childSelector.SelectOptionAsync("player");
        await nestedChild.Locator("[data-condition-field='PlayerSignal']").SelectOptionAsync("PlayerTag");
        var comparison = nestedChild.Locator("[data-condition-field='Operator']");
        await Assertions.Expect(comparison.Locator("option[value='Contains']")).ToBeHiddenAsync();
        await comparison.SelectOptionAsync("NotEqual");
        await nestedChild.Locator("[data-condition-field='ExpectedValue']").FillAsync("vpn-exempt");
        await nested.Locator(":scope > [data-condition-group] > .row [data-condition-field='GroupOperator']").SelectOptionAsync("Or");
        await nested.Locator(":scope > .row [data-action='add-siege-vpn-condition']").ClickAsync();
        await Assertions.Expect(nestedChildren).ToHaveCountAsync(2);
        await nested.Locator(":scope > .row [data-action='add-siege-player-condition']").ClickAsync();
        await Assertions.Expect(nestedChildren).ToHaveCountAsync(3);
        await nestedChildren.Nth(2).Locator(":scope > .row [data-action='remove-siege-condition']").ClickAsync();
        await Assertions.Expect(nestedChildren).ToHaveCountAsync(2);
        await Assertions.Expect(root).ToHaveCountAsync(1);
        await page.Locator("#SiegeMode_DurationMinutes").FillAsync("75");

        await SaveSettings(page);
        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#SiegeMode_DurationMinutes")).ToHaveValueAsync("75");
        await Assertions.Expect(page.Locator("#global-siege-rules > [data-siege-rule-row]")).ToHaveCountAsync(1);
        var reloaded = page.Locator("#global-siege-rules > [data-siege-rule-row]").First;
        await Assertions.Expect(reloaded.Locator("[data-rule-field='Id']")).ToHaveValueAsync("nested-test");
        await Assertions.Expect(reloaded.Locator("[name='SiegeMode.Rules[0].Condition.Conditions[1].GroupOperator']")).ToHaveValueAsync("Or");
        await Assertions.Expect(reloaded.Locator("[name='SiegeMode.Rules[0].Condition.Conditions[1].Conditions[0].ExpectedValue']")).ToHaveValueAsync("vpn-exempt");
        using var saved = JsonDocument.Parse(scenario.SiegeJson!);
        var condition = saved.RootElement.GetProperty("rules")[0].GetProperty("condition");
        Assert.Equal("Or", condition.GetProperty("conditions")[1].GetProperty("groupOperator").GetString());
        Assert.Equal(2, condition.GetProperty("conditions")[1].GetProperty("conditions").GetArrayLength());
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Editor_DefaultTagOutsideCatalog_CanBeSaved()
    {
        var scenario = new SettingsScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);

        await SaveSettings(page);
        using var saved = JsonDocument.Parse(scenario.SiegeJson!);
        var condition = saved.RootElement.GetProperty("rules")[1].GetProperty("condition");
        Assert.Equal("vpn-exempt", condition.GetProperty("conditions")[2].GetProperty("expectedValue").GetString());
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Editor_RemovingEveryRule_PersistsAnEmptyList()
    {
        var scenario = new SettingsScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);
        var rules = page.Locator("#global-siege-rules > [data-siege-rule-row]");
        await Assertions.Expect(rules).ToHaveCountAsync(2);
        await rules.Nth(1).Locator("[data-action='remove-siege-rule']").ClickAsync();
        await rules.Nth(0).Locator("[data-action='remove-siege-rule']").ClickAsync();
        await Assertions.Expect(rules).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#SiegeMode_SubmittedRuleCount")).ToHaveValueAsync("0");

        await SaveSettings(page);
        using var saved = JsonDocument.Parse(scenario.SiegeJson!);
        Assert.Equal(0, saved.RootElement.GetProperty("rules").GetArrayLength());
        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#global-siege-rules > [data-siege-rule-row]")).ToHaveCountAsync(0);
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Editor_MissingOrMalformedSiegeSection_DoesNotOverwriteSettings(bool missing)
    {
        var scenario = new SettingsScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);
        if (missing)
        {
            await page.Locator("#siege-mode-tab").EvaluateAsync("element => element.remove()");
        }
        else
        {
            await page.Locator("form[action='/GlobalSettings']").EvaluateAsync(
                "form => form.addEventListener('submit', () => document.getElementById('SiegeMode_SubmittedRuleCount').value = 'invalid')");
        }

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save Settings", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/GlobalSettings");
        Assert.Equal(200, response.Status);
        await Assertions.Expect(page.Locator(".validation-summary-errors")).ToContainTextAsync("Siege Mode settings section is missing or incomplete");
        Assert.Null(scenario.SiegeJson);
        browser.AssertNoBrowserErrors();
    }

    private async static Task OpenSettings(IPage page, Uri baseAddress)
    {
        await page.GotoAsync(new Uri(baseAddress, "/GlobalSettings").AbsoluteUri);
        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#siege-mode-tab")).ToBeVisibleAsync();
    }

    private async static Task SaveSettings(IPage page)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save Settings", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/GlobalSettings");
        Assert.Equal(302, response.Status);
        await Assertions.Expect(page.Locator("#globalSettingsTabs")).ToBeVisibleAsync();
    }

    private sealed class SettingsScenario
    {
        private readonly ConcurrentDictionary<string, string> configurations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Mock<IRepositoryApiClient> repository = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };

        public SettingsScenario()
        {
            Mock.Get(repository.Object.GlobalConfigurations.V1)
                .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ApiResult<CollectionModel<ConfigurationDto>>(
                    HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(
                        new CollectionModel<ConfigurationDto>([.. configurations.Select(entry => CreateConfiguration(entry.Key, entry.Value))]))));
            Mock.Get(repository.Object.GlobalConfigurations.V1)
                .Setup(api => api.UpsertConfiguration(It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpsertConfigurationDto, CancellationToken>((ns, dto, _) => configurations[ns] = dto.Configuration)
                .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
            Mock.Get(repository.Object.Tags.V1)
                .Setup(api => api.GetTags(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(
                    HttpStatusCode.OK, new ApiResponse<CollectionModel<TagDto>>(
                        new CollectionModel<TagDto>([new TagDto { TagId = Guid.NewGuid(), Name = "SeniorAdmin" }]))));
        }

        public string? SiegeJson => configurations.GetValueOrDefault(SiegeModeSettingsConstants.Namespace);

        public void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
        }

        private static ConfigurationDto CreateConfiguration(string ns, string configuration)
        {
            return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
            {
                Namespace = ns,
                Configuration = configuration,
                LastModifiedUtc = DateTime.UtcNow
            }))!;
        }
    }
}
