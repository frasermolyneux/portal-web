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
}
