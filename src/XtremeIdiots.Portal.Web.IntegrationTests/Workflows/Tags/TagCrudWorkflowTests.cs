using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Tags;

public class TagCrudWorkflowTests
{
    [Fact]
    public async Task GameAdmin_CreatesUserDefinedTag()
    {
        var scenario = new TagScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.GameAdmin,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Tags/Create").AbsoluteUri);

        await fixture.Page.GetByTestId("tag-name").FillAsync("VIP");
        await fixture.Page.GetByTestId("tag-description").FillAsync("Priority player");
        await fixture.Page.GetByTestId("tag-html").FillAsync("<span class=\"badge\">VIP</span>");
        Assert.True(await fixture.Page.GetByTestId("tag-user-defined").IsCheckedAsync());
        await fixture.Page.GetByTestId("tag-create-submit").ClickAsync();

        await fixture.Page.WaitForURLAsync("**/Tags");
        var command = Assert.Single(scenario.CreatedTags);
        Assert.Equal("VIP", command.Name);
        Assert.Equal("Priority player", command.Description);
        Assert.Equal("<span class=\"badge\">VIP</span>", command.TagHtml);
        Assert.True(command.UserDefined);
        Assert.True(await fixture.Page.GetByText("The tag 'VIP' has been successfully created").IsVisibleAsync());
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task GameAdmin_EditsExistingTag()
    {
        var scenario = new TagScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.GameAdmin,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, $"/Tags/Edit/{scenario.Tag.TagId}").AbsoluteUri);

        Assert.Equal("Existing tag", await fixture.Page.GetByTestId("tag-name").InputValueAsync());
        await fixture.Page.GetByTestId("tag-name").FillAsync("Updated tag");
        await fixture.Page.GetByTestId("tag-description").FillAsync("Updated description");
        await fixture.Page.GetByTestId("tag-edit-submit").ClickAsync();

        await fixture.Page.WaitForURLAsync("**/Tags");
        var command = Assert.Single(scenario.UpdatedTags);
        Assert.Equal(scenario.Tag.TagId, command.TagId);
        Assert.Equal("Updated tag", command.Name);
        Assert.Equal("Updated description", command.Description);
        Assert.Equal(scenario.Tag.TagHtml, command.TagHtml);
        Assert.Equal(scenario.Tag.UserDefined, command.UserDefined);
        Assert.True(await fixture.Page.GetByText("The tag 'Updated tag' has been successfully updated").IsVisibleAsync());
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task GameAdmin_DeletesUserDefinedTag()
    {
        var scenario = new TagScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.GameAdmin,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, $"/Tags/Delete/{scenario.Tag.TagId}").AbsoluteUri);

        Assert.True(await fixture.Page.GetByText("Are you sure you want to delete this tag?").IsVisibleAsync());
        await fixture.Page.GetByTestId("tag-delete-submit").ClickAsync();

        await fixture.Page.WaitForURLAsync("**/Tags");
        Assert.Equal(scenario.Tag.TagId, Assert.Single(scenario.DeletedTagIds));
        Assert.True(await fixture.Page.GetByText("The tag 'Existing tag' has been successfully deleted").IsVisibleAsync());
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Moderator_CannotOpenCreateFormDirectly()
    {
        var scenario = new TagScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.Moderator,
            scenario.ConfigureServices);

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Tags/Create").AbsoluteUri);

        Assert.NotNull(response);
        Assert.Equal(403, response.Status);
        Assert.False(await fixture.Page.GetByTestId("tag-create-form").IsVisibleAsync());
        Assert.Empty(scenario.CreatedTags);
    }
}
