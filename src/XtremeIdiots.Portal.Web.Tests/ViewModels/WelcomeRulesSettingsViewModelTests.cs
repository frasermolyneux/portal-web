using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class WelcomeRulesSettingsViewModelTests
{
    private const string Rule = """{"id":"global","enabled":true,"order":0,"condition":{"conditionId":"always","parameters":{}},"actions":[{"id":"message","typeId":"message.private","parameters":{"message":"Welcome"},"dependsOnActionIds":[]}],"afterMatch":"StopProcessingRules","timing":{"connectionDelaySeconds":9}}""";

    [Fact]
    public void EffectiveTiming_RespectsGlobalRuleAndServerRuleOverridePrecedence()
    {
        var global = new WelcomeRulesGlobalSettingsViewModel { RulesJson = $"[{Rule}]" };
        var server = new WelcomeRulesServerSettingsViewModel
        {
            ConnectionDelaySeconds = 12,
            RuleOverridesJson = """[{"ruleId":"global","timing":{"freshnessSeconds":90}}]"""
        };
        Assert.True(WelcomeRulesSettingsMapper.TryGetEffectiveTimings(global, server, out var timings));
        Assert.Equal(9, timings["global"].ConnectionDelaySeconds);
        Assert.Equal(90, timings["global"].FreshnessSeconds);
    }

    [Fact]
    public void EffectiveTiming_ReportsInvalidMergedWindow()
    {
        var global = new WelcomeRulesGlobalSettingsViewModel { RulesJson = $"[{Rule}]" };
        var server = new WelcomeRulesServerSettingsViewModel { FreshnessSeconds = 8 };
        Assert.False(WelcomeRulesSettingsMapper.TryGetEffectiveTimings(global, server, out _));
    }

    [Fact]
    public void Validation_RejectsInvalidGlobalTiming()
    {
        var model = new WelcomeRulesGlobalSettingsViewModel { ConnectionDelaySeconds = 15, FreshnessSeconds = 15 };
        Assert.NotEmpty(model.Validate(new ValidationContext(model)));
    }

    [Fact]
    public void ParserAndSerializer_RoundTripWelcomeRulesAndEnvelopeExtensions()
    {
        var json = """{"schemaVersion":1,"featureId":"welcome-messages","enabled":true,"timing":{"connectionDelaySeconds":5,"freshnessSeconds":120},"rules":["""
            + Rule + """],"future":{"value":"preserved"}}""";
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = WelcomeRulesSettingsConstants.Namespace,
            Configuration = json
        }))!;
        var parser = new NamespaceSettingsParser();
        Assert.True(parser.TryParseWelcomeRulesSettings(config, out _, out var errors), string.Join("; ", errors));
        var model = new GlobalSettingsViewModel();
        parser.PopulateGlobalSettingsViewModel(model, config, Mock.Of<ILogger>());
        var saved = Assert.Single(new NamespaceSettingsSerializer().BuildGlobalSettingsConfigurations(model),
            item => item.Namespace == WelcomeRulesSettingsConstants.Namespace);
        using var document = JsonDocument.Parse(saved.Configuration);
        Assert.Equal("preserved", document.RootElement.GetProperty("future").GetProperty("value").GetString());
        Assert.Equal("global", document.RootElement.GetProperty("rules")[0].GetProperty("id").GetString());
    }
}
