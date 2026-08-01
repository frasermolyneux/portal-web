using Microsoft.Playwright;
using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Tags;

[Binding]
public sealed class TagManagementSteps
{
    private BrowserFixture? browser;
    private string? profile;
    private IResponse? response;
    private TagScenario? scenario;

    [Given("an isolated tag scenario")]
    public void GivenAnIsolatedTagScenario()
    {
        scenario = new TagScenario();
    }

    [Given("I am authenticated as a game admin")]
    public void GivenIAmAuthenticatedAsAGameAdmin()
    {
        profile = TestPrincipalProfiles.GameAdmin;
    }

    [Given("I am authenticated as a moderator")]
    public void GivenIAmAuthenticatedAsAModerator()
    {
        profile = TestPrincipalProfiles.Moderator;
    }

    [When("I create the VIP user-defined tag")]
    public async Task WhenICreateTheVipUserDefinedTag()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(new Uri(Browser.Host.BaseAddress, "/Tags/Create").AbsoluteUri);
        await Browser.Page.GetByTestId("tag-name").FillAsync("VIP");
        await Browser.Page.GetByTestId("tag-description").FillAsync("Priority player");
        await Browser.Page.GetByTestId("tag-html").FillAsync("<span class=\"badge\">VIP</span>");
        Assert.True(await Browser.Page.GetByTestId("tag-user-defined").IsCheckedAsync());
        await Browser.Page.GetByTestId("tag-create-submit").ClickAsync();
        await Browser.Page.WaitForURLAsync("**/Tags");
    }

    [When("I update the existing tag")]
    public async Task WhenIUpdateTheExistingTag()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(new Uri(Browser.Host.BaseAddress, $"/Tags/Edit/{Scenario.Tag.TagId}").AbsoluteUri);
        Assert.Equal("Existing tag", await Browser.Page.GetByTestId("tag-name").InputValueAsync());
        await Browser.Page.GetByTestId("tag-name").FillAsync("Updated tag");
        await Browser.Page.GetByTestId("tag-description").FillAsync("Updated description");
        await Browser.Page.GetByTestId("tag-edit-submit").ClickAsync();
        await Browser.Page.WaitForURLAsync("**/Tags");
    }

    [When("I delete the existing user-defined tag")]
    public async Task WhenIDeleteTheExistingUserDefinedTag()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(new Uri(Browser.Host.BaseAddress, $"/Tags/Delete/{Scenario.Tag.TagId}").AbsoluteUri);
        Assert.True(await Browser.Page.GetByText("Are you sure you want to delete this tag?").IsVisibleAsync());
        await Browser.Page.GetByTestId("tag-delete-submit").ClickAsync();
        await Browser.Page.WaitForURLAsync("**/Tags");
    }

    [When("I navigate directly to the create-tag form")]
    public async Task WhenINavigateDirectlyToTheCreateTagForm()
    {
        await StartBrowserAsync();
        response = await Browser.Page.GotoAsync(new Uri(Browser.Host.BaseAddress, "/Tags/Create").AbsoluteUri);
    }

    [Then("the VIP tag command should contain the expected details")]
    public void ThenTheVipTagCommandShouldContainTheExpectedDetails()
    {
        var command = Assert.Single(Scenario.CreatedTags);
        Assert.Equal("VIP", command.Name);
        Assert.Equal("Priority player", command.Description);
        Assert.Equal("<span class=\"badge\">VIP</span>", command.TagHtml);
        Assert.True(command.UserDefined);
    }

    [Then("successful tag creation feedback should be displayed")]
    public async Task ThenSuccessfulTagCreationFeedbackShouldBeDisplayed()
    {
        Assert.True(await Browser.Page.GetByText("The tag 'VIP' has been successfully created").IsVisibleAsync());
    }

    [Then("the update tag command should preserve all expected details")]
    public void ThenTheUpdateTagCommandShouldPreserveAllExpectedDetails()
    {
        var command = Assert.Single(Scenario.UpdatedTags);
        Assert.Equal(Scenario.Tag.TagId, command.TagId);
        Assert.Equal("Updated tag", command.Name);
        Assert.Equal("Updated description", command.Description);
        Assert.Equal(Scenario.Tag.TagHtml, command.TagHtml);
        Assert.Equal(Scenario.Tag.UserDefined, command.UserDefined);
    }

    [Then("successful tag update feedback should be displayed")]
    public async Task ThenSuccessfulTagUpdateFeedbackShouldBeDisplayed()
    {
        Assert.True(await Browser.Page.GetByText("The tag 'Updated tag' has been successfully updated").IsVisibleAsync());
    }

    [Then("the delete tag command should contain the existing tag identifier")]
    public void ThenTheDeleteTagCommandShouldContainTheExistingTagIdentifier()
    {
        Assert.Equal(Scenario.Tag.TagId, Assert.Single(Scenario.DeletedTagIds));
    }

    [Then("successful tag deletion feedback should be displayed")]
    public async Task ThenSuccessfulTagDeletionFeedbackShouldBeDisplayed()
    {
        Assert.True(await Browser.Page.GetByText("The tag 'Existing tag' has been successfully deleted").IsVisibleAsync());
    }

    [Then("tag creation access should be forbidden")]
    public async Task ThenTagCreationAccessShouldBeForbidden()
    {
        Assert.NotNull(response);
        Assert.Equal(403, response.Status);
        Assert.False(await Browser.Page.GetByTestId("tag-create-form").IsVisibleAsync());
    }

    [Then("no tag should have been created")]
    public void ThenNoTagShouldHaveBeenCreated()
    {
        Assert.Empty(Scenario.CreatedTags);
    }

    [Then("the browser should report no errors")]
    public void ThenTheBrowserShouldReportNoErrors()
    {
        Browser.AssertNoBrowserErrors();
    }

    [AfterScenario]
    public async Task DisposeBrowserAsync()
    {
        if (browser is not null)
            await browser.DisposeAsync();
    }

    private BrowserFixture Browser => browser ?? throw new InvalidOperationException("The browser has not been started.");

    private TagScenario Scenario => scenario ?? throw new InvalidOperationException("The tag scenario has not been configured.");

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(
            profile ?? throw new InvalidOperationException("The authentication profile has not been configured."),
            Scenario.ConfigureServices);
    }
}
