using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.ViewModels;

/// <summary>
/// Regression coverage for global-settings saves being silently blocked by a required-tag validation
/// error. Validation runs during model binding, before the controller applies the tags catalog, so the
/// catalog-available flag must default to false (dormant) rather than true (which flagged every assigned
/// tag as unavailable and blocked the save with an invisible error).
/// </summary>
public class GlobalSettingsRequiredTagsValidationTests
{
    private static bool HasTagUnavailableError(GlobalSettingsViewModel model)
    {
        return model.Validate(new ValidationContext(model))
            .Any(result => result.ErrorMessage is not null && result.ErrorMessage.Contains("not available", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiredTagsCatalogAvailable_DefaultsToFalse()
    {
        Assert.False(new ChatCommandGlobalSettingsViewModel().RequiredTagsCatalogAvailable);
        Assert.False(new WelcomeMessageGlobalSettingsViewModel().RequiredTagsCatalogAvailable);
    }

    [Fact]
    public void RequiredTagsCatalogAvailable_ServerVariants_DefaultToFalse()
    {
        Assert.False(new ChatCommandServerSettingsViewModel().RequiredTagsCatalogAvailable);
        Assert.False(new WelcomeMessageServerSettingsViewModel().RequiredTagsCatalogAvailable);
    }

    [Fact]
    public void Validate_WithRequiredTag_BeforeCatalogApplied_DoesNotFlagTag()
    {
        var model = new GlobalSettingsViewModel();
        model.ChatCommands.DefaultRequiredTags = "SeniorAdmin";

        Assert.False(HasTagUnavailableError(model));
    }

    [Fact]
    public void Validate_WithKnownRequiredTag_AfterCatalogApplied_DoesNotFlagTag()
    {
        var model = new GlobalSettingsViewModel();
        model.ChatCommands.DefaultRequiredTags = "SeniorAdmin";

        model.ApplyAvailableRequiredTags(
            [new RequiredTagOptionViewModel { Name = "SeniorAdmin" }],
            requiredTagsCatalogAvailable: true);

        Assert.False(HasTagUnavailableError(model));
    }

    [Fact]
    public void Validate_WithUnknownRequiredTag_AfterCatalogApplied_FlagsTag()
    {
        var model = new GlobalSettingsViewModel();
        model.ChatCommands.DefaultRequiredTags = "NoSuchTag";

        model.ApplyAvailableRequiredTags(
            [new RequiredTagOptionViewModel { Name = "SeniorAdmin" }],
            requiredTagsCatalogAvailable: true);

        // With the catalog applied, the tag validation logic remains intact.
        Assert.True(HasTagUnavailableError(model));
    }
}
