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

    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SettingsMappings", fileName);
        return File.ReadAllText(path);
    }
}
