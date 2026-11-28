using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Moderation;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class ModerationEditorCompatibilityTests
{
    [Theory]
    [InlineData(/*lang=json,strict*/ """{"contentSafetySeverityThreshold":3,"minMessageLength":5}""", true, false)]
    [InlineData(/*lang=json,strict*/ """{"protectedNameEnforcementEnabled":"false","minMessageLength":5}""", false, false)]
    [InlineData(/*lang=json,strict*/ """{"protectedNameEnforcementEnabled":false}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"minMessageLength":5,"rules":{"enabled":false}}""", false, true)]
    [InlineData(/*lang=json,strict*/ """{"minMessageLength":5,"rules":null}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"schemaVersion":2}""", false, true)]
    [InlineData(/*lang=json,strict*/ """{"minMessageLength":0}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"minMessageLength":5,"MinMessageLength":6}""", false, true)]
    [InlineData("invalid", true, true)]
    public void RequiresReadOnly_DetectsUneditableDocuments(string json, bool global, bool expected)
    {
        Assert.Equal(expected, ModerationEditorCompatibility.RequiresReadOnly(json, global));
    }

    [Fact]
    public void ProtectedModeration_IsNeitherUpsertedNorDeleted()
    {
        var serializer = new NamespaceSettingsSerializer();
        var global = new GlobalSettingsViewModel { ModerationReadOnly = true };
        var globalConfigurations = serializer.BuildGlobalSettingsConfigurations(global);
        Assert.DoesNotContain(globalConfigurations, entry => entry.Namespace == ModerationSettingsConstants.Namespace);
        Assert.DoesNotContain(ModerationSettingsConstants.Namespace, serializer.DeletedNamespaces);

        var server = new GameServerEditViewModel { ModerationReadOnly = true };
        server.GameServer.AgentEnabled = true;
        server.ModerationProtectedNameEnforcementEnabled = false;
        var serverConfigurations = serializer.BuildGameServerConfigurations(server, false, false, false);
        Assert.DoesNotContain(serverConfigurations, entry => entry.Namespace == ModerationSettingsConstants.Namespace);
        Assert.DoesNotContain(ModerationSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }
}
