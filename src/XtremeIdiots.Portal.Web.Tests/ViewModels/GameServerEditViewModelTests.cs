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
    public void Validate_WhenBroadcastMessageExceeds120Characters_ReturnsValidationError()
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

        Assert.False(isValid);
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
