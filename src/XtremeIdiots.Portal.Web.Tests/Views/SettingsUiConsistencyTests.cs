using System.IO;

namespace XtremeIdiots.Portal.Web.Tests.Views;

public class SettingsUiConsistencyTests
{
    [Fact]
    public void TriStateOverrideComponent_UsesInheritEnabledDisabledOptions()
    {
        var componentMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/Shared/Components/TriStateOverrideSelect.cshtml");

        Assert.Contains("@Model.InheritLabel", componentMarkup);
        Assert.Contains(">Enabled<", componentMarkup);
        Assert.Contains(">Disabled<", componentMarkup);
        Assert.Contains("aria-describedby", componentMarkup);
    }

    [Fact]
    public void WelcomeMessagesConfiguration_UsesSharedTriStateComponentForEnabledOverride()
    {
        var viewMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_WelcomeMessagesConfiguration.cshtml");

        Assert.Contains("Views/Shared/Components/TriStateOverrideSelect.cshtml", viewMarkup);
        Assert.Contains("FieldName = \"WelcomeMessages.Enabled\"", viewMarkup);
        Assert.Contains("Inherit global", viewMarkup);
    }

    [Fact]
    public void BroadcastAndServerListSections_UseConsistentGlobalAndServerHeadings()
    {
        var globalBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_BroadcastsConfiguration.cshtml");
        var serverBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_BroadcastsConfiguration.cshtml");
        var globalServerList = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ServerListConfiguration.cshtml");
        var serverServerList = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_ServerListConfiguration.cshtml");

        Assert.Contains("<h5><i class=\"fa-solid fa-fw fa-bullhorn\" aria-hidden=\"true\"></i> Broadcasts</h5>", globalBroadcasts);
        Assert.Contains("<h5>Broadcasts</h5>", serverBroadcasts);
        Assert.Contains("Server List", globalServerList);
        Assert.Contains("<h5>Server List</h5>", serverServerList);
    }

    [Fact]
    public void GlobalSettingsIndex_UsesSharedSettingsRowManagerScript()
    {
        var viewMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/Index.cshtml");

        Assert.Contains("~/js/settings-row-manager.js", viewMarkup);
        Assert.Contains("XISettingsRowManager.initializeMessageList", viewMarkup);
        Assert.DoesNotContain("function reindexGlobalChatCommandRows", viewMarkup);
        Assert.DoesNotContain("function wireGlobalChatCommandRow", viewMarkup);
    }

    [Fact]
    public void GlobalSettingsIndex_UsesTabbedLayoutAlignedWithServerSettings()
    {
        var viewMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/Index.cshtml");

        Assert.Contains("id=\"globalSettingsTabs\"", viewMarkup);
        Assert.Contains("General", viewMarkup);
        Assert.Contains("Agent", viewMarkup);
        Assert.Contains("Broadcasts", viewMarkup);
        Assert.Contains("Chat Commands", viewMarkup);
        Assert.Contains("Welcome Messages", viewMarkup);
        Assert.Contains("Chat Moderation", viewMarkup);
        Assert.Contains("Event Processing", viewMarkup);
        Assert.Contains("Ban File Sync", viewMarkup);
        Assert.Contains("Server List", viewMarkup);
    }

    [Fact]
    public void GameServersEdit_UsesSharedSettingsRowManagerScript()
    {
        var viewMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/Edit.cshtml");

        Assert.Contains("~/js/settings-row-manager.js", viewMarkup);
        Assert.Contains("XISettingsRowManager.initializeMessageList", viewMarkup);
        Assert.DoesNotContain("function reindexMessageRows", viewMarkup);
        Assert.DoesNotContain("function wireMessageRow", viewMarkup);
    }

    [Fact]
    public void GameServerBroadcastsAndChatCommandEnabledUseTriStateOverrideSelect()
    {
        var broadcastsMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_BroadcastsConfiguration.cshtml");
        var chatCommandsMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_ChatCommandsConfiguration.cshtml");

        Assert.Contains("Views/Shared/Components/TriStateOverrideSelect.cshtml", broadcastsMarkup);
        Assert.Contains("FieldName = \"BroadcastsEnabled\"", broadcastsMarkup);

        Assert.Contains("Views/Shared/Components/TriStateOverrideSelect.cshtml", chatCommandsMarkup);
        Assert.Contains("FieldName = $\"ChatCommands.Commands[{i}].Enabled\"", chatCommandsMarkup);
    }

    [Fact]
    public void GlobalChatCommandsEnabled_IsBinaryAndDoesNotUseTriStateInheritOption()
    {
        var chatCommandsMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ChatCommandsConfiguration.cshtml");

        Assert.DoesNotContain("Use default", chatCommandsMarkup);
        Assert.DoesNotContain("Inherit global", chatCommandsMarkup);
        Assert.DoesNotContain("Views/Shared/Components/TriStateOverrideSelect.cshtml", chatCommandsMarkup);
        Assert.Contains("<option value=\"true\">Enabled</option>", chatCommandsMarkup);
        Assert.Contains("<option value=\"false\">Disabled</option>", chatCommandsMarkup);
    }

    [Fact]
    public void GlobalWelcomeAndChatDefaults_CheckboxMarkupPlacesHiddenFallbackAfterCheckbox()
    {
        var welcomeMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_WelcomeMessagesConfiguration.cshtml");
        var chatMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ChatCommandsConfiguration.cshtml");

        var welcomeCheckboxIndex = welcomeMarkup.IndexOf("name=\"WelcomeMessages.Enabled\" value=\"true\"", StringComparison.Ordinal);
        var welcomeHiddenIndex = welcomeMarkup.IndexOf("name=\"WelcomeMessages.Enabled\" value=\"false\"", StringComparison.Ordinal);
        Assert.True(welcomeCheckboxIndex >= 0, "WelcomeMessages.Enabled checkbox markup not found.");
        Assert.True(welcomeHiddenIndex > welcomeCheckboxIndex,
            "WelcomeMessages.Enabled hidden fallback must be rendered after the checkbox.");

        var chatCheckboxIndex = chatMarkup.IndexOf("name=\"ChatCommands.DefaultsEnabled\" value=\"true\"", StringComparison.Ordinal);
        var chatHiddenIndex = chatMarkup.IndexOf("name=\"ChatCommands.DefaultsEnabled\" value=\"false\"", StringComparison.Ordinal);
        Assert.True(chatCheckboxIndex >= 0, "ChatCommands.DefaultsEnabled checkbox markup not found.");
        Assert.True(chatHiddenIndex > chatCheckboxIndex,
            "ChatCommands.DefaultsEnabled hidden fallback must be rendered after the checkbox.");
    }

    [Fact]
    public void ServerWelcomeInheritGlobalRules_CheckboxMarkupPlacesHiddenFallbackAfterCheckbox()
    {
        var welcomeServerMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_WelcomeMessagesConfiguration.cshtml");

        var checkboxIndex = welcomeServerMarkup.IndexOf("name=\"WelcomeMessages.InheritGlobalRules\" value=\"true\"", StringComparison.Ordinal);
        var hiddenIndex = welcomeServerMarkup.IndexOf("name=\"WelcomeMessages.InheritGlobalRules\" value=\"false\"", StringComparison.Ordinal);

        Assert.True(checkboxIndex >= 0, "WelcomeMessages.InheritGlobalRules checkbox markup not found.");
        Assert.True(hiddenIndex > checkboxIndex,
            "WelcomeMessages.InheritGlobalRules hidden fallback must be rendered after the checkbox.");
    }

    [Fact]
    public void DynamicEnabledRows_PlaceHiddenFallbackAfterCheckboxInGlobalSettings()
    {
        var globalBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_BroadcastsConfiguration.cshtml");
        var globalChatCommands = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ChatCommandsConfiguration.cshtml");
        var globalWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_WelcomeMessagesConfiguration.cshtml");

        AssertCheckboxBeforeHidden(globalBroadcasts,
            "name=\"BroadcastMessages[@i].Enabled\" value=\"true\"",
            "data-field=\"enabled-hidden\" name=\"BroadcastMessages[@i].Enabled\"",
            "Global broadcasts row enabled");

        AssertCheckboxBeforeHidden(globalChatCommands,
            "name=\"ChatCommands.Commands[@i].Messages[@j].Enabled\" value=\"true\"",
            "name=\"ChatCommands.Commands[@i].Messages[@j].Enabled\" value=\"false\"",
            "Global chat command message row enabled");

        AssertCheckboxBeforeHidden(globalWelcomeMessages,
            "name=\"WelcomeMessages.Rules[@i].Enabled\" value=\"true\"",
            "name=\"WelcomeMessages.Rules[@i].Enabled\" value=\"false\"",
            "Global welcome rule row enabled");
    }

    [Fact]
    public void DynamicEnabledRows_PlaceHiddenFallbackAfterCheckboxInServerSettings()
    {
        var serverBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_BroadcastsConfiguration.cshtml");
        var serverChatCommands = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_ChatCommandsConfiguration.cshtml");
        var serverWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_WelcomeMessagesConfiguration.cshtml");

        AssertCheckboxBeforeHidden(serverBroadcasts,
            "name=\"BroadcastMessages[@i].Enabled\" value=\"true\"",
            "data-field=\"enabled-hidden\" name=\"BroadcastMessages[@i].Enabled\"",
            "Server broadcasts row enabled");

        AssertCheckboxBeforeHidden(serverChatCommands,
            "name=\"ChatCommands.Commands[@i].Messages[@j].Enabled\" value=\"true\"",
            "name=\"ChatCommands.Commands[@i].Messages[@j].Enabled\" value=\"false\"",
            "Server chat command message row enabled");

        AssertCheckboxBeforeHidden(serverWelcomeMessages,
            "name=\"WelcomeMessages.LocalRules[@i].Enabled\"",
            "data-field=\"enabled-hidden\" name=\"WelcomeMessages.LocalRules[@i].Enabled\"",
            "Server welcome local rule row enabled");
    }

    [Fact]
    public void WelcomeMessagePriorityFields_AreHiddenButRemainInDomForBindingAndReindexing()
    {
        var globalWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_WelcomeMessagesConfiguration.cshtml");
        var serverWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_WelcomeMessagesConfiguration.cshtml");

        AssertContainsNormalized(
            globalWelcomeMessages,
            "<div class=\"col-lg-2 d-none\"> <div class=\"mb-3\"> <label asp-for=\"WelcomeMessages.Rules[i].Priority\" class=\"form-label\"></label>",
            "Global welcome existing-rule priority should be hidden.");
        AssertContainsNormalized(
            globalWelcomeMessages,
            "<div class=\"col-lg-2 d-none\"> <div class=\"mb-3\"> <label class=\"form-label\" data-field=\"priority-label\" for=\"WelcomeMessages_Rules_0__Priority\">Priority</label>",
            "Global welcome template-rule priority should be hidden.");

        AssertContainsNormalized(
            serverWelcomeMessages,
            "<div class=\"col-lg-2 d-none\"> <div class=\"mb-3\"> <label asp-for=\"WelcomeMessages.LocalRules[i].Priority\" class=\"form-label\"></label>",
            "Server welcome local existing-rule priority should be hidden.");
        AssertContainsNormalized(
            serverWelcomeMessages,
            "<div class=\"col-lg-2 d-none\"> <div class=\"mb-3\"> <label class=\"form-label\" data-field=\"priority-label\" for=\"WelcomeMessages_LocalRules_0__Priority\">Priority</label>",
            "Server welcome local template-rule priority should be hidden.");

        AssertContainsNormalized(
            serverWelcomeMessages,
            "<div class=\"col-lg-3 d-none\"> <div class=\"mb-3\"> <label asp-for=\"WelcomeMessages.RuleOverrides[i].Priority\" class=\"form-label\"></label>",
            "Server welcome override existing-rule priority should be hidden.");
        AssertContainsNormalized(
            serverWelcomeMessages,
            "<div class=\"col-lg-3 d-none\"> <div class=\"mb-3\"> <label class=\"form-label\" data-field=\"priority-label\" for=\"WelcomeMessages_RuleOverrides_0__Priority\">Priority Override</label>",
            "Server welcome override template-rule priority should be hidden.");

        Assert.Contains("asp-for=\"WelcomeMessages.Rules[i].Priority\"", globalWelcomeMessages);
        Assert.Contains("name=\"WelcomeMessages.Rules[0].Priority\"", globalWelcomeMessages);
        Assert.Contains("data-field=\"priority\"", globalWelcomeMessages);

        Assert.Contains("asp-for=\"WelcomeMessages.LocalRules[i].Priority\"", serverWelcomeMessages);
        Assert.Contains("name=\"WelcomeMessages.LocalRules[0].Priority\"", serverWelcomeMessages);
        Assert.Contains("asp-for=\"WelcomeMessages.RuleOverrides[i].Priority\"", serverWelcomeMessages);
        Assert.Contains("name=\"WelcomeMessages.RuleOverrides[0].Priority\"", serverWelcomeMessages);
        Assert.Contains("data-field=\"priority\"", serverWelcomeMessages);
    }

    [Fact]
    public void ChatCommandFuMessages_GlobalAndServerOfferMultilineImport()
    {
        var globalMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ChatCommandsConfiguration.cshtml");
        var serverMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_ChatCommandsConfiguration.cshtml");

        Assert.Contains("Import multiline", globalMarkup);
        Assert.Contains("data-import-button-id", globalMarkup);

        Assert.Contains("Import multiline", serverMarkup);
        Assert.Contains("data-import-button-id", serverMarkup);
    }

    [Fact]
    public void SettingsRowManager_SupportsMultilineImportSplitFilterAndCap()
    {
        var managerScript = ReadRepoFile("src/XtremeIdiots.Portal.Web/wwwroot/js/settings-row-manager.js");

        Assert.Contains("split(/\\r?\\n/)", managerScript);
        Assert.Contains("line.trim()", managerScript);
        Assert.Contains("line.length > 0", managerScript);
        Assert.Contains("maxImportRows", managerScript);
        Assert.Contains("showMultilineImportDialog", managerScript);
        Assert.Contains("data-action=\"import\"", managerScript);
        Assert.Contains("data-action=\"cancel\"", managerScript);
        Assert.Contains("aria-labelledby", managerScript);
    }

    [Fact]
    public void GlobalAndServerSettings_DoNotUseSliderStyleSwitchWrappersForEnabledCheckboxes()
    {
        var globalBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_BroadcastsConfiguration.cshtml");
        var globalChatCommands = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_ChatCommandsConfiguration.cshtml");
        var globalWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_WelcomeMessagesConfiguration.cshtml");

        var serverGeneral = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_GeneralConfiguration.cshtml");
        var serverBroadcasts = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_BroadcastsConfiguration.cshtml");
        var serverChatCommands = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_ChatCommandsConfiguration.cshtml");
        var serverWelcomeMessages = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_WelcomeMessagesConfiguration.cshtml");

        Assert.DoesNotContain("form-switch", globalBroadcasts);
        Assert.DoesNotContain("form-switch", globalChatCommands);
        Assert.DoesNotContain("form-switch", globalWelcomeMessages);

        Assert.DoesNotContain("form-switch", serverGeneral);
        Assert.DoesNotContain("form-switch", serverBroadcasts);
        Assert.DoesNotContain("form-switch", serverChatCommands);
        Assert.DoesNotContain("form-switch", serverWelcomeMessages);
    }

    [Fact]
    public void Cod4xCommandSections_IncludeCollectionIndexFieldsForStableModelBinding()
    {
        var globalCod4xMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GlobalSettings/_Cod4xConfiguration.cshtml");
        var serverCod4xMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/GameServers/ConfigurationSections/_Cod4xConfiguration.cshtml");

        Assert.Contains("name=\"Cod4xCommands.Index\"", globalCod4xMarkup);
        Assert.Contains("name=\"Cod4xCommands.Index\"", serverCod4xMarkup);
    }

    [Fact]
    public void MapRotationsIndex_UsesServerActivationColumnHeading()
    {
        var viewMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/Views/MapRotations/Index.cshtml");

        Assert.Contains("<th scope=\"col\">Server Activation</th>", viewMarkup);
        Assert.DoesNotContain("<th scope=\"col\">Servers</th>", viewMarkup);
    }

    [Fact]
    public void MapRotationsIndexScript_RendersServerActivationBadgesWithExpectedStates()
    {
        var scriptMarkup = ReadRepoFile("src/XtremeIdiots.Portal.Web/wwwroot/js/map-rotations-index.js");

        Assert.Contains("<span class=\"badge bg-secondary\">Inactive</span>", scriptMarkup);
        Assert.Contains("<span class=\"badge bg-success\">Active on ", scriptMarkup);
        Assert.Contains("var count = Number(data) || 0;", scriptMarkup);
        Assert.Contains("var serverLabel = count === 1 ? 'server' : 'servers';", scriptMarkup);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(absolutePath);
    }

    private static void AssertCheckboxBeforeHidden(string markup, string checkboxNeedle, string hiddenNeedle, string context)
    {
        var normalizedMarkup = NormalizeWhitespace(markup);
        var normalizedCheckboxNeedle = NormalizeWhitespace(checkboxNeedle);
        var normalizedHiddenNeedle = NormalizeWhitespace(hiddenNeedle);

        var checkboxIndex = normalizedMarkup.IndexOf(normalizedCheckboxNeedle, StringComparison.Ordinal);
        var hiddenIndex = normalizedMarkup.IndexOf(normalizedHiddenNeedle, StringComparison.Ordinal);

        Assert.True(checkboxIndex >= 0, $"{context}: checkbox markup not found.");
        Assert.True(hiddenIndex > checkboxIndex, $"{context}: hidden fallback must be rendered after the checkbox.");
    }

    private static void AssertContainsNormalized(string markup, string needle, string context)
    {
        var normalizedMarkup = NormalizeWhitespace(markup);
        var normalizedNeedle = NormalizeWhitespace(needle);

        Assert.Contains(normalizedNeedle, normalizedMarkup, StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
