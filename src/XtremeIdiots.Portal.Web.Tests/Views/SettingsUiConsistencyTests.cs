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

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(absolutePath);
    }
}
