using System.Text.Json;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class SettingsMappingFixtureTests
{
    [Fact]
    public void ChatCommands_GlobalFixture_RoundTripsWithParity()
    {
        var fixtureJson = ReadFixture("chatCommands.global.json");
        var sourceModel = new ChatCommandGlobalSettingsViewModel();

        PopulateGlobalChat(sourceModel, fixtureJson);
        var builtJson = BuildGlobalChat(sourceModel);

        var roundTripModel = new ChatCommandGlobalSettingsViewModel();
        PopulateGlobalChat(roundTripModel, builtJson);

        Assert.Equal(sourceModel.DefaultsEnabled, roundTripModel.DefaultsEnabled);
        Assert.Equal(sourceModel.DefaultFreshnessSeconds, roundTripModel.DefaultFreshnessSeconds);
        Assert.Equal(sourceModel.ReadOnlyFreshnessSeconds, roundTripModel.ReadOnlyFreshnessSeconds);
        Assert.Equal(sourceModel.MutatingFreshnessSeconds, roundTripModel.MutatingFreshnessSeconds);
        Assert.Equal(sourceModel.DefaultRequiredTags, roundTripModel.DefaultRequiredTags);

        var sourceFu = sourceModel.Commands.Single(command => command.Name == "fu");
        var roundTripFu = roundTripModel.Commands.Single(command => command.Name == "fu");

        Assert.Equal(sourceFu.Enabled, roundTripFu.Enabled);
        Assert.Equal(sourceFu.FreshnessSeconds, roundTripFu.FreshnessSeconds);
        Assert.Equal(sourceFu.RequiredTags, roundTripFu.RequiredTags);
        Assert.Equal(sourceFu.Messages.Single().Message, roundTripFu.Messages.Single().Message);
        Assert.Equal(sourceFu.Messages.Single().Enabled, roundTripFu.Messages.Single().Enabled);
    }

    [Fact]
    public void ChatCommands_ServerFixture_RoundTripsWithParity()
    {
        var fixtureJson = ReadFixture("chatCommands.server.json");
        var sourceModel = new ChatCommandServerSettingsViewModel();

        PopulateServerChat(sourceModel, fixtureJson);
        var builtJson = BuildServerChat(sourceModel);

        var roundTripModel = new ChatCommandServerSettingsViewModel();
        PopulateServerChat(roundTripModel, builtJson);

        var sourceFu = sourceModel.Commands.Single(command => command.Name == "fu");
        var roundTripFu = roundTripModel.Commands.Single(command => command.Name == "fu");

        Assert.Equal(sourceFu.OverrideEnabled, roundTripFu.OverrideEnabled);
        Assert.Equal(sourceFu.OverrideFreshness, roundTripFu.OverrideFreshness);
        Assert.Equal(sourceFu.OverrideRequiredTags, roundTripFu.OverrideRequiredTags);
        Assert.Equal(sourceFu.OverrideMessages, roundTripFu.OverrideMessages);
        Assert.Equal(sourceFu.Enabled, roundTripFu.Enabled);
        Assert.Equal(sourceFu.FreshnessSeconds, roundTripFu.FreshnessSeconds);
        Assert.Equal(sourceFu.RequiredTags, roundTripFu.RequiredTags);
        Assert.Equal(sourceFu.Messages.Single().Message, roundTripFu.Messages.Single().Message);
    }

    [Fact]
    public void WelcomeMessages_GlobalFixture_RoundTripsWithParity()
    {
        var fixtureJson = ReadFixture("welcomeMessages.global.json");
        var sourceModel = new WelcomeMessageGlobalSettingsViewModel();

        PopulateGlobalWelcome(sourceModel, fixtureJson);
        var builtJson = BuildGlobalWelcome(sourceModel);

        var roundTripModel = new WelcomeMessageGlobalSettingsViewModel();
        PopulateGlobalWelcome(roundTripModel, builtJson);

        Assert.Equal(sourceModel.Enabled, roundTripModel.Enabled);
        Assert.Equal(sourceModel.CountryFallback, roundTripModel.CountryFallback);
        Assert.Equal(sourceModel.DefaultConnectionDelaySeconds, roundTripModel.DefaultConnectionDelaySeconds);
        Assert.Equal(sourceModel.StaleThresholdSeconds, roundTripModel.StaleThresholdSeconds);

        var sourceRule = sourceModel.Rules.Single();
        var roundTripRule = roundTripModel.Rules.Single();

        Assert.Equal(sourceRule.Id, roundTripRule.Id);
        Assert.Equal(sourceRule.Enabled, roundTripRule.Enabled);
        Assert.Equal(sourceRule.Priority, roundTripRule.Priority);
        Assert.Equal(sourceRule.Visibility, roundTripRule.Visibility);
        Assert.Equal(sourceRule.MessageTemplate, roundTripRule.MessageTemplate);
        Assert.Equal(sourceRule.RequiredTagsCsv, roundTripRule.RequiredTagsCsv);
        Assert.Equal(sourceRule.ConnectionDelaySeconds, roundTripRule.ConnectionDelaySeconds);
    }

    [Fact]
    public void WelcomeMessages_ServerFixture_RoundTripsWithParity()
    {
        var fixtureJson = ReadFixture("welcomeMessages.server.json");
        var sourceModel = new WelcomeMessageServerSettingsViewModel();

        PopulateServerWelcome(sourceModel, fixtureJson);
        var builtJson = BuildServerWelcome(sourceModel);

        var roundTripModel = new WelcomeMessageServerSettingsViewModel();
        PopulateServerWelcome(roundTripModel, builtJson);

        Assert.Equal(sourceModel.Enabled, roundTripModel.Enabled);
        Assert.Equal(sourceModel.InheritGlobalRules, roundTripModel.InheritGlobalRules);
        Assert.Equal(sourceModel.CountryFallback, roundTripModel.CountryFallback);
        Assert.Equal(sourceModel.DefaultConnectionDelaySeconds, roundTripModel.DefaultConnectionDelaySeconds);
        Assert.Equal(sourceModel.StaleThresholdSeconds, roundTripModel.StaleThresholdSeconds);

        var sourceLocalRule = sourceModel.LocalRules.Single();
        var roundTripLocalRule = roundTripModel.LocalRules.Single();

        Assert.Equal(sourceLocalRule.Id, roundTripLocalRule.Id);
        Assert.Equal(sourceLocalRule.RequiredTagsCsv, roundTripLocalRule.RequiredTagsCsv);

        var sourceOverrideRule = sourceModel.RuleOverrides.Single();
        var roundTripOverrideRule = roundTripModel.RuleOverrides.Single();

        Assert.Equal(sourceOverrideRule.Id, roundTripOverrideRule.Id);
        Assert.Equal(sourceOverrideRule.OverrideRequiredTags, roundTripOverrideRule.OverrideRequiredTags);
        Assert.Equal(sourceOverrideRule.RequiredTagsCsv, roundTripOverrideRule.RequiredTagsCsv);
        Assert.Equal(sourceOverrideRule.ConnectionDelaySeconds, roundTripOverrideRule.ConnectionDelaySeconds);
    }

    [Fact]
    public void ChatCommands_ServerDependencyDisableAndReenable_PreservesValues()
    {
        var model = new ChatCommandServerSettingsViewModel();
        var command = model.Commands.Single(item => item.Name == "fu");

        command.OverrideRequiredTags = true;
        command.RequiredTags = "vip, staff";
        command.OverrideMessages = true;
        command.Messages =
        [
            new BroadcastMessageViewModel { Message = "message-one", Enabled = true }
        ];

        var enabledJson = BuildServerChat(model);
        using var enabledDoc = JsonDocument.Parse(enabledJson);
        var enabledFu = enabledDoc.RootElement.GetProperty("commands").GetProperty("fu");
        Assert.Equal(2, enabledFu.GetProperty("requiredTags").GetArrayLength());
        Assert.Equal("message-one", enabledFu.GetProperty("settings").GetProperty("messages")[0].GetProperty("message").GetString());

        command.OverrideRequiredTags = false;
        command.OverrideMessages = false;

        var disabledJson = BuildServerChat(model);
        using var disabledDoc = JsonDocument.Parse(disabledJson);
        var disabledCommands = disabledDoc.RootElement.GetProperty("commands");
        Assert.False(disabledCommands.TryGetProperty("fu", out _));

        command.OverrideRequiredTags = true;
        command.OverrideMessages = true;

        var reenabledJson = BuildServerChat(model);
        using var reenabledDoc = JsonDocument.Parse(reenabledJson);
        var reenabledFu = reenabledDoc.RootElement.GetProperty("commands").GetProperty("fu");
        Assert.Equal(2, reenabledFu.GetProperty("requiredTags").GetArrayLength());
        Assert.Equal("message-one", reenabledFu.GetProperty("settings").GetProperty("messages")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void WelcomeMessages_ServerDependencyDisableAndReenable_PreservesValues()
    {
        var model = new WelcomeMessageServerSettingsViewModel
        {
            RuleOverrides =
            [
                new WelcomeMessageRuleOverrideEntryViewModel
                {
                    Id = "global-rule",
                    OverrideRequiredTags = true,
                    RequiredTagsCsv = "vip, trusted"
                }
            ]
        };

        var enabledJson = BuildServerWelcome(model);
        using var enabledDoc = JsonDocument.Parse(enabledJson);
        var enabledOverride = enabledDoc.RootElement.GetProperty("ruleOverrides")[0];
        Assert.Equal(2, enabledOverride.GetProperty("requiredTags").GetArrayLength());

        model.RuleOverrides[0].OverrideRequiredTags = false;

        var disabledJson = BuildServerWelcome(model);
        using var disabledDoc = JsonDocument.Parse(disabledJson);
        var disabledOverride = disabledDoc.RootElement.GetProperty("ruleOverrides")[0];
        Assert.False(disabledOverride.TryGetProperty("requiredTags", out _));

        model.RuleOverrides[0].OverrideRequiredTags = true;

        var reenabledJson = BuildServerWelcome(model);
        using var reenabledDoc = JsonDocument.Parse(reenabledJson);
        var reenabledOverride = reenabledDoc.RootElement.GetProperty("ruleOverrides")[0];
        Assert.Equal(2, reenabledOverride.GetProperty("requiredTags").GetArrayLength());
    }

    private static void PopulateGlobalChat(ChatCommandGlobalSettingsViewModel model, string json)
    {
        using var document = JsonDocument.Parse(json);
        ChatCommandSettingsJsonMapper.PopulateGlobal(model, document.RootElement);
    }

    private static void PopulateServerChat(ChatCommandServerSettingsViewModel model, string json)
    {
        using var document = JsonDocument.Parse(json);
        ChatCommandSettingsJsonMapper.PopulateServer(model, document.RootElement);
    }

    private static string BuildGlobalChat(ChatCommandGlobalSettingsViewModel model)
    {
        return ChatCommandSettingsJsonMapper.BuildGlobalConfigurationJson(model);
    }

    private static string BuildServerChat(ChatCommandServerSettingsViewModel model)
    {
        return ChatCommandSettingsJsonMapper.BuildServerConfigurationJson(model);
    }

    private static void PopulateGlobalWelcome(WelcomeMessageGlobalSettingsViewModel model, string json)
    {
        using var document = JsonDocument.Parse(json);
        WelcomeMessageSettingsJsonMapper.PopulateGlobal(model, document.RootElement);
    }

    private static void PopulateServerWelcome(WelcomeMessageServerSettingsViewModel model, string json)
    {
        using var document = JsonDocument.Parse(json);
        WelcomeMessageSettingsJsonMapper.PopulateServer(model, document.RootElement);
    }

    private static string BuildGlobalWelcome(WelcomeMessageGlobalSettingsViewModel model)
    {
        return WelcomeMessageSettingsJsonMapper.BuildGlobalConfigurationJson(model);
    }

    private static string BuildServerWelcome(WelcomeMessageServerSettingsViewModel model)
    {
        return WelcomeMessageSettingsJsonMapper.BuildServerConfigurationJson(model);
    }

    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SettingsMappings", fileName);
        return File.ReadAllText(path);
    }
}
