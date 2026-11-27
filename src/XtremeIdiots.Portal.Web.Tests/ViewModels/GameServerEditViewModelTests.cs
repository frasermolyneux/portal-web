using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.FileTransport;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Shared;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class GameServerEditViewModelTests
{
    [Fact]
    public void Validate_UndefinedSftpAuthenticationType_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.FileTransportConfigSftpAuthenticationType = (SftpAuthenticationType)999;
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(GameServerEditViewModel.FileTransportConfigSftpAuthenticationType)));
    }

    [Fact]
    public void BroadcastsIntervalSeconds_DefaultsToInherit()
    {
        var model = new GameServerEditViewModel();

        Assert.Null(model.BroadcastsIntervalSeconds);
    }

    [Fact]
    public void Validate_DuplicateBroadcastIds_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.BroadcastMessages =
        [
            new BroadcastMessageViewModel { Id = "same", Message = "One" },
            new BroadcastMessageViewModel { Id = "same", Message = "Two" }
        ];
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains("BroadcastMessages[1].Id"));
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("line\nbreak")]
    [InlineData("null\0character")]
    public void Validate_UnsafeBroadcastId_ReturnsValidationError(string id)
    {
        var model = CreateValidModel();
        model.BroadcastMessages = [new BroadcastMessageViewModel { Id = id, Message = "Message" }];
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains("BroadcastMessages[0].Id"));
    }

    [Fact]
    public void Validate_OverlongBroadcastId_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.BroadcastMessages = [new BroadcastMessageViewModel { Id = new string('a', 129), Message = "Message" }];
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains("BroadcastMessages[0].Id"));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\0second")]
    public void Validate_UnsafeBroadcastText_ReturnsValidationError(string text)
    {
        var server = CreateValidModel();
        server.BroadcastMessages = [new BroadcastMessageViewModel { Message = text }];
        var global = new GlobalSettingsViewModel { BroadcastMessages = [new BroadcastMessageViewModel { Message = text }] };
        var serverResults = new List<ValidationResult>();
        var globalResults = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(server, new ValidationContext(server), serverResults, true));
        Assert.Contains(serverResults, result => result.MemberNames.Contains("BroadcastMessages[0].Message"));
        Assert.False(Validator.TryValidateObject(global, new ValidationContext(global), globalResults, true));
        Assert.Contains(globalResults, result => result.MemberNames.Contains("BroadcastMessages[0].Message"));
    }

    [Fact]
    public void Validate_WhenBroadcastMessageExceeds120Characters_IsValid()
    {
        var model = CreateValidModel();
        model.BroadcastMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = new string('a', 121),
                Enabled = true
            }
        ];

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_WhenBroadcastMessageIs120Characters_IsValid()
    {
        var model = CreateValidModel();
        model.BroadcastMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = new string('a', 120),
                Enabled = true
            }
        ];

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_WhenBroadcastMessageIsBlank_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.BroadcastMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = " ",
                Enabled = true
            }
        ];

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, r => r.MemberNames.Contains("BroadcastMessages[0].Message"));
    }

    [Fact]
    public void Validate_WhenBroadcastMessagesIsNull_IsValid()
    {
        var model = CreateValidModel();
        model.BroadcastMessages = null!;

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_WhenFunnyMessageExceeds120Characters_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.FunnyMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = new string('a', 121),
                Enabled = true
            }
        ];

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_WhenFunnyMessageIs120Characters_IsValid()
    {
        var model = CreateValidModel();
        model.FunnyMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = new string('a', 120),
                Enabled = true
            }
        ];

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_WhenBroadcastMessagesIsNull_StillValidatesFunnyMessages()
    {
        var model = CreateValidModel();
        model.BroadcastMessages = null!;
        model.FunnyMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = new string('a', 121),
                Enabled = true
            }
        ];

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_WhenScreenshotMonitoringEnabledWithoutDirectory_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.GameServer.AgentEnabled = true;
        model.ScreenshotConfigEnabled = true;
        model.ScreenshotConfigDirectoryPath = string.Empty;

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, r => r.MemberNames.Contains(nameof(GameServerEditViewModel.ScreenshotConfigDirectoryPath)));
    }

    [Fact]
    public void ScreenshotConfigPollIntervalSeconds_DefaultsTo60()
    {
        var model = new GameServerEditViewModel();

        Assert.Equal(60, model.ScreenshotConfigPollIntervalSeconds);
    }

    [Fact]
    public void Validate_VpnProtectionServerOverride_AllowsInheritedEnablement()
    {
        var model = CreateValidModel();
        model.VpnProtection.Enabled = null;
        model.VpnProtection.InheritGlobalRules = true;
        model.VpnProtection.RuleOverridesJson = /*lang=json,strict*/ """
            [{
              "ruleId": "global-vpn",
              "enabled": false,
              "condition": null,
              "actions": null,
              "afterMatch": null
            }]
            """;
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.True(isValid, string.Join("; ", validationResults.Select(static result => result.ErrorMessage)));
    }

    [Fact]
    public void Validate_VpnProtectionServerSettingsWithMalformedRulesJson_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.VpnProtection.LocalRulesJson = "{";
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(VpnProtectionServerSettingsViewModel.LocalRulesJson)));
    }

    [Fact]
    public void ApplyAvailableRequiredTags_Cod4xPowerMappings_NewTagsDefaultToZero()
    {
        var model = new GameServerEditViewModel
        {
            Cod4xPowerTagMappingsJson = /*lang=json,strict*/ """
            [
              { "tag": "Moderator", "power": 40, "enabled": true }
            ]
            """
        };

        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "Moderator", DisplayName = "Moderator" },
            new RequiredTagOptionViewModel { Name = "NewTag", DisplayName = "NewTag" }
        ]);

        Assert.Equal(2, model.Cod4xPowerTagMappings.Count);

        var existingTag = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(40, existingTag.Power);

        var newTag = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "NewTag", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, newTag.Power);
    }

    [Fact]
    public void ApplyAvailableRequiredTags_Cod4xPowerMappings_RemovedTagsAreDroppedAndJsonUpdated()
    {
        var model = new GameServerEditViewModel
        {
            Cod4xPowerTagMappings =
            [
                new Cod4xPowerTagMappingViewModel { Tag = "KeepTag", Power = 30 },
                new Cod4xPowerTagMappingViewModel { Tag = "RemovedTag", Power = 75 }
            ]
        };

        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "KeepTag", DisplayName = "KeepTag" }
        ]);

        Assert.Single(model.Cod4xPowerTagMappings);
        Assert.Equal("KeepTag", model.Cod4xPowerTagMappings[0].Tag);
        Assert.Equal(30, model.Cod4xPowerTagMappings[0].Power);
        Assert.DoesNotContain("RemovedTag", model.Cod4xPowerTagMappingsJson, StringComparison.OrdinalIgnoreCase);
    }

    private static GameServerEditViewModel CreateValidModel()
    {
        return new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                Title = "Test Server",
                Hostname = "localhost",
                QueryPort = 28960
            }
        };
    }
}
