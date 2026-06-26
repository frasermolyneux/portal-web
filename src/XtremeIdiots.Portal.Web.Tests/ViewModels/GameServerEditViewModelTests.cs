using System.ComponentModel.DataAnnotations;
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
