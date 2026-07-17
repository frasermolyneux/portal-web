using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class GameServerEditViewModelTests
{
    [Fact]
    public void BroadcastsIntervalSeconds_DefaultsTo500()
    {
        var model = new GameServerEditViewModel();

        Assert.Equal(500, model.BroadcastsIntervalSeconds);
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
    public void Validate_UnsupportedGameWithDestructiveVpnRule_DefersGameSupportToController()
    {
        var model = CreateValidModel();
        model.GameServer.GameType = GameType.Insurgency;
        model.VpnProtection.LocalRules =
        [
            new VpnProtectionRuleViewModel
            {
                Id = "vpn",
                Signal = VpnProtectionSignal.ProxyCheckIsVpn,
                Operator = VpnProtectionComparisonOperator.Equal,
                ExpectedValue = "true",
                Action = VpnProtectionAction.Ban
            }
        ];
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.True(isValid);
        Assert.DoesNotContain(validationResults, result =>
            result.MemberNames.Contains("VpnProtection.LocalRules[0].Action"));
    }

    [Fact]
    public void Validate_ExcludedVpnTagOutsideLoadedCatalog_ReturnsValidationError()
    {
        var model = CreateValidModel();
        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "Trusted VPN", DisplayName = "Trusted VPN" }
        ]);
        model.VpnProtection.ExcludedPlayerTagsCsv = "Unknown Tag";
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.ErrorMessage?.Contains("Unknown Tag", StringComparison.Ordinal) == true);
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
