using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class RulesCapabilityResolverTests
{
    [Theory]
    [InlineData(RulesEngineCatalogueV1.VpnProtectionFeature)]
    [InlineData(RulesEngineCatalogueV1.SiegeModeFeature)]
    public void Resolve_UnsupportedGame_ExplicitlyRejectsAllFeatureChoices(string featureId)
    {
        var choices = RulesCapabilityResolver.Resolve(GameType.Insurgency, featureId, null);

        Assert.NotEmpty(choices);
        Assert.False(choices["vpn.intelligence"]);
        Assert.False(choices["moderation.observation"]);
        Assert.False(choices["player.kick"]);
        Assert.All(choices.Values, Assert.False);
    }

    [Theory]
    [InlineData(RulesEngineCatalogueV1.VpnProtectionFeature)]
    [InlineData(RulesEngineCatalogueV1.SiegeModeFeature)]
    public void Resolve_NoSnapshot_LeavesProviderDependentChoicesUnknown(string featureId)
    {
        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4x, featureId, null);

        Assert.True(choices["vpn.intelligence"]);
        Assert.True(choices["player.tags"]);
        Assert.True(choices["moderation.observation"]);
        Assert.True(choices["moderation.ban"]);
        Assert.False(choices.ContainsKey("player.kick"));
        Assert.False(choices.ContainsKey("message.public"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Resolve_StaleSnapshot_IgnoresPreviouslyReportedCapabilities(bool expired, bool hasFreshStatus)
    {
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameType = GameType.CallOfDuty4x,
            HasFreshStatus = hasFreshStatus,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1),
            RuntimeCapabilities = [RulesEngineCatalogueV1.Cod4xPlayerDropCapability],
            IntegrationCapabilities = [RulesEngineCatalogueV1.RconPublicChatCapability]
        };

        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4x, RulesEngineCatalogueV1.VpnProtectionFeature, snapshot);

        Assert.True(choices["vpn.intelligence"]);
        Assert.True(choices["moderation.observation"]);
        Assert.False(choices.ContainsKey("player.kick"));
        Assert.False(choices.ContainsKey("message.public"));
    }

    [Fact]
    public void Resolve_SnapshotForDifferentGame_DoesNotSupplyCapabilities()
    {
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameType = GameType.CallOfDuty4,
            HasFreshStatus = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            IntegrationCapabilities = [RulesEngineCatalogueV1.RconPlayerDropCapability]
        };

        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4x, RulesEngineCatalogueV1.SiegeModeFeature, snapshot);

        Assert.True(choices["vpn.intelligence"]);
        Assert.False(choices.ContainsKey("player.kick"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_FreshPluginOrRconProvider_SupportsKickAndRejectsMissingCapabilities(bool plugin)
    {
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameType = GameType.CallOfDuty4x,
            HasFreshStatus = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RuntimeCapabilities = plugin ? [RulesEngineCatalogueV1.Cod4xPlayerDropCapability] : [],
            IntegrationCapabilities = plugin ? [] : [RulesEngineCatalogueV1.RconPlayerDropCapability]
        };

        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4x, RulesEngineCatalogueV1.VpnProtectionFeature, snapshot);

        Assert.True(choices["vpn.intelligence"]);
        Assert.True(choices["moderation.observation"]);
        Assert.True(choices["player.kick"]);
        Assert.True(choices["moderation.kick-with-record"]);
        Assert.False(choices["message.public"]);
        Assert.False(choices["message.private"]);
    }

    [Fact]
    public void Resolve_FreshSnapshotWithoutCapabilities_ExplicitlyRejectsProviderDependentChoices()
    {
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameType = GameType.CallOfDuty4x,
            HasFreshStatus = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4x, RulesEngineCatalogueV1.SiegeModeFeature, snapshot);

        Assert.True(choices["player.tags"]);
        Assert.True(choices["moderation.ban"]);
        Assert.False(choices["player.kick"]);
        Assert.False(choices["message.public"]);
    }

    [Fact]
    public void Resolve_PluginCapabilityForOtherGame_DoesNotEnablePluginProvider()
    {
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameType = GameType.CallOfDuty4,
            HasFreshStatus = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RuntimeCapabilities = [RulesEngineCatalogueV1.Cod4xPlayerDropCapability]
        };

        var choices = RulesCapabilityResolver.Resolve(GameType.CallOfDuty4, RulesEngineCatalogueV1.VpnProtectionFeature, snapshot);

        Assert.True(choices["vpn.intelligence"]);
        Assert.False(choices["player.kick"]);
    }

    [Fact]
    public void FindNewUnsupported_UnchangedUnsupportedAndUnknownDeclarations_ArePreserved()
    {
        var saved = Document("rule-1", "unknown.condition", "kick-1", "player.kick");
        var proposed = Document("rule-1", "unknown.condition", "kick-1", "player.kick");
        var compatibility = new Dictionary<string, bool> { ["player.kick"] = false };

        Assert.Empty(RulesCapabilityResolver.FindNewUnsupported(saved, proposed, compatibility));
    }

    [Fact]
    public void FindNewUnsupported_AddedUnknownConditionAndUnsupportedAction_AreRejected()
    {
        var compatibility = new Dictionary<string, bool> { ["player.kick"] = false };
        var proposed = Document("rule-1", "unknown.condition", "kick-1", "player.kick");

        var errors = RulesCapabilityResolver.FindNewUnsupported(new FeatureRuleSetOverrideDocument(), proposed, compatibility);

        Assert.Contains(errors, error => error.Contains("condition 'unknown.condition'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("action 'player.kick'", StringComparison.Ordinal));
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void FindNewUnsupported_AddedSupportedDeclarations_AreAccepted()
    {
        var compatibility = new Dictionary<string, bool>
        {
            ["vpn.intelligence"] = true,
            ["moderation.observation"] = true
        };
        var proposed = Document("rule-1", "vpn.intelligence", "observe-1", "moderation.observation");

        Assert.Empty(RulesCapabilityResolver.FindNewUnsupported(new FeatureRuleSetOverrideDocument(), proposed, compatibility));
    }

    [Fact]
    public void FindNewUnsupported_ChangingRuleOrActionId_DoesNotReuseSavedDeclaration()
    {
        var compatibility = new Dictionary<string, bool> { ["player.kick"] = false, ["unknown.condition"] = false };
        var saved = Document("rule-1", "unknown.condition", "kick-1", "player.kick");
        var newRule = Document("rule-2", "unknown.condition", "kick-1", "player.kick");
        var newAction = Document("rule-1", "unknown.condition", "kick-2", "player.kick");

        var ruleErrors = RulesCapabilityResolver.FindNewUnsupported(saved, newRule, compatibility);
        var actionErrors = RulesCapabilityResolver.FindNewUnsupported(saved, newAction, compatibility);

        Assert.Equal(2, ruleErrors.Count);
        Assert.Single(actionErrors);
        Assert.Contains("action 'player.kick'", actionErrors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void FindNewUnsupported_AdditionalOccurrence_IsRejectedEvenWhenAlreadySaved()
    {
        var compatibility = new Dictionary<string, bool> { ["player.kick"] = false };
        var saved = Document("rule-1", "vpn.intelligence", "kick-1", "player.kick");
        var proposed = Document("rule-1", "vpn.intelligence", "kick-1", "player.kick");
        proposed.Rules[0].Actions.Add(new RuleActionDefinition { Id = "kick-1", TypeId = "player.kick" });
        compatibility["vpn.intelligence"] = true;

        var errors = RulesCapabilityResolver.FindNewUnsupported(saved, proposed, compatibility);

        Assert.Single(errors);
        Assert.Contains("action 'player.kick'", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void FindNewUnsupported_AdditionalNestedConditionOccurrence_IsRejected()
    {
        var saved = Document("rule-1", "unknown.condition", "observe-1", "moderation.observation");
        var proposed = Document("rule-1", "unknown.condition", "observe-1", "moderation.observation");
        proposed.Rules[0].Condition = new RuleConditionNode
        {
            Conditions =
            [
                new RuleConditionNode { ConditionId = "unknown.condition" },
                new RuleConditionNode { ConditionId = "unknown.condition" }
            ]
        };
        var compatibility = new Dictionary<string, bool> { ["moderation.observation"] = true };

        var errors = RulesCapabilityResolver.FindNewUnsupported(saved, proposed, compatibility);

        Assert.Single(errors);
        Assert.Contains("condition 'unknown.condition'", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void FindNewUnsupported_MovingDeclarationFromLocalRuleToOverride_IsRejected()
    {
        var compatibility = new Dictionary<string, bool> { ["player.kick"] = false };
        var saved = Document("rule-1", "vpn.intelligence", "kick-1", "player.kick");
        var proposed = new FeatureRuleSetOverrideDocument
        {
            RuleOverrides =
            [
                new RuleDefinitionOverride
                {
                    RuleId = "rule-1",
                    Actions = [new RuleActionDefinition { Id = "kick-1", TypeId = "player.kick" }]
                }
            ]
        };

        var errors = RulesCapabilityResolver.FindNewUnsupported(saved, proposed, compatibility);

        Assert.Single(errors);
        Assert.Contains("action 'player.kick'", errors[0], StringComparison.Ordinal);
    }

    private static FeatureRuleSetOverrideDocument Document(string ruleId, string conditionId, string actionId, string actionTypeId)
        => new()
        {
            Rules =
            [
                new RuleDefinition
                {
                    Id = ruleId,
                    Condition = new RuleConditionNode { ConditionId = conditionId },
                    Actions = [new RuleActionDefinition { Id = actionId, TypeId = actionTypeId }]
                }
            ]
        };
}
