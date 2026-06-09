using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

public class WelcomeMessageServerSettingsViewModelTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void EnabledSetter_TracksTriStateOverrideSemantics(bool? postedValue, bool expectedInherit)
    {
        var model = new WelcomeMessageServerSettingsViewModel
        {
            Enabled = postedValue
        };

        Assert.Equal(postedValue, model.Enabled);
        Assert.Equal(postedValue, model.EnabledOverride.Value);
        Assert.Equal(expectedInherit, model.EnabledOverride.IsInherit);
    }
}
