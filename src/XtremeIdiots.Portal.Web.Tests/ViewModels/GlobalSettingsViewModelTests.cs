using System.ComponentModel.DataAnnotations;
using System.Linq;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class GlobalSettingsViewModelTests
{
    [Fact]
    public void Validate_WhenFunnyMessageExceeds120Characters_ReturnsValidationError()
    {
        var model = new GlobalSettingsViewModel
        {
            FunnyMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = new string('a', 121),
                    Enabled = true
                }
            ]
        };

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_WhenFunnyMessageIs120Characters_IsValid()
    {
        var model = new GlobalSettingsViewModel
        {
            FunnyMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = new string('a', 120),
                    Enabled = true
                }
            ]
        };

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_WhenBroadcastMessageIsBlank_ReturnsValidationError()
    {
        var model = new GlobalSettingsViewModel
        {
            BroadcastMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = "   ",
                    Enabled = true
                }
            ]
        };

        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.ErrorMessage == "Broadcast message is required." &&
            result.MemberNames.Contains("BroadcastMessages[0].Message"));
    }

    [Fact]
    public void ChatCommands_DefaultCommandsIncludeAliases()
    {
        var model = new ChatCommandGlobalSettingsViewModel();

        var commands = model.Commands.Single(x => x.Name == "commands");

        Assert.Equal(["!help"], commands.Aliases);
    }

    [Fact]
    public void GlobalSettings_DefaultCod4xCommandsIncludePortalPluginHealth()
    {
        var model = new GlobalSettingsViewModel();

        var portalPluginHealth = model.Cod4xCommands.Single(static command =>
            string.Equals(command.Name, "portalpluginhealth", StringComparison.OrdinalIgnoreCase));

        Assert.True(portalPluginHealth.Enabled);
        Assert.Equal(98, portalPluginHealth.MinPower);
    }

    [Fact]
    public void ApplyAvailableRequiredTags_Cod4xPowerMappings_NewTagsDefaultToZero()
    {
        var model = new GlobalSettingsViewModel
        {
            Cod4xPowerTagMappingsJson = /*lang=json,strict*/ """
            [
              { "tag": "HeadAdmin", "power": 90, "enabled": true }
            ]
            """
        };

        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "HeadAdmin", DisplayName = "HeadAdmin" },
            new RequiredTagOptionViewModel { Name = "NewTag", DisplayName = "NewTag" }
        ]);

        Assert.Equal(2, model.Cod4xPowerTagMappings.Count);

        var existingTag = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "HeadAdmin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(90, existingTag.Power);

        var newTag = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "NewTag", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, newTag.Power);
    }

    [Fact]
    public void ApplyAvailableRequiredTags_Cod4xPowerMappings_RemovedTagsAreDroppedAndJsonUpdated()
    {
        var model = new GlobalSettingsViewModel
        {
            Cod4xPowerTagMappings =
            [
                new Cod4xPowerTagMappingViewModel { Tag = "KeepTag", Power = 25 },
                new Cod4xPowerTagMappingViewModel { Tag = "RemovedTag", Power = 80 }
            ]
        };

        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "KeepTag", DisplayName = "KeepTag" }
        ]);

        Assert.Single(model.Cod4xPowerTagMappings);
        Assert.Equal("KeepTag", model.Cod4xPowerTagMappings[0].Tag);
        Assert.Equal(25, model.Cod4xPowerTagMappings[0].Power);
        Assert.DoesNotContain("RemovedTag", model.Cod4xPowerTagMappingsJson, StringComparison.OrdinalIgnoreCase);
    }
}
