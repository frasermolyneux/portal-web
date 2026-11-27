using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.UserManagement;

[Trait("Category", "Browser")]
public sealed class ActivityLogFilterUiTests
{
    [Fact]
    public async Task UserSelectionPersistsAndResetRemovesServerFilter()
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new { DisplayName = "Member One", XtremeIdiotsForumId = "123" }))!;
        repository.Setup(r => r.UserProfiles.V1.GetUserProfiles("Member", null, 0, 15, UserProfilesOrder.DisplayNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<UserProfileDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<UserProfileDto>>(new CollectionModel<UserProfileDto>([profile]))));
        repository.Setup(r => r.UserProfiles.V1.GetUserProfiles("Other", null, 0, 15, UserProfilesOrder.DisplayNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<UserProfileDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<UserProfileDto>>(new CollectionModel<UserProfileDto>([]))));
        var activity = new Mock<IActivityLogService>();
        activity.Setup(s => s.QueryEventsAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyList<ActivityLogCategory>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityLogQueryResult());
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, services =>
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton(activity.Object);
        });

        await fixture.Page.RunAndWaitForResponseAsync(
            () => fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/User/ActivityLog").AbsoluteUri),
            response => IsActivityResponse(response, null));
        await fixture.Page.Locator("#filterUser").FillAsync("Member");
        await Assertions.Expect(fixture.Page.GetByRole(AriaRole.Option, new() { Name = "Member One" })).ToBeVisibleAsync();
        var picker = fixture.Page.Locator("#filterUser");
        var suggestions = fixture.Page.Locator("#filterUser-suggestions");
        await Assertions.Expect(picker).ToHaveAttributeAsync("role", "combobox");
        await Assertions.Expect(picker).ToHaveAttributeAsync("aria-controls", "filterUser-suggestions");
        await Assertions.Expect(picker).ToHaveAttributeAsync("aria-expanded", "true");
        var inputBox = await picker.BoundingBoxAsync();
        var suggestionsBox = await suggestions.BoundingBoxAsync();
        Assert.NotNull(inputBox);
        Assert.NotNull(suggestionsBox);
        Assert.InRange(suggestionsBox.Y - (inputBox.Y + inputBox.Height), 0, 3);
        await picker.PressAsync("ArrowDown");
        await Assertions.Expect(picker).ToHaveAttributeAsync("aria-activedescendant", "filterUser-suggestions-0");
        await Assertions.Expect(fixture.Page.Locator("#filterUser-suggestions-0")).ToHaveAttributeAsync("aria-selected", "true");
        await fixture.Page.RunAndWaitForResponseAsync(
            () => picker.PressAsync("Enter"),
            response => IsActivityResponse(response, "123"));
        await Assertions.Expect(fixture.Page.Locator("#filterUserId")).ToHaveValueAsync("123");
        await Assertions.Expect(picker).ToHaveAttributeAsync("aria-expanded", "false");

        await fixture.Page.RunAndWaitForResponseAsync(
            () => fixture.Page.ReloadAsync(),
            response => IsActivityResponse(response, "123"));
        await Assertions.Expect(fixture.Page.Locator("#filterUser")).ToHaveValueAsync("Member One");
        await fixture.Page.RunAndWaitForResponseAsync(
            () => fixture.Page.Locator("#filterUser").FillAsync("Other"),
            response => IsActivityResponse(response, null));
        await Assertions.Expect(fixture.Page.Locator("#filterUserId")).ToHaveValueAsync("");

        await fixture.Page.RunAndWaitForResponseAsync(
            () => fixture.Page.Locator("#resetFilters").ClickAsync(),
            response => IsActivityResponse(response, null));
        await Assertions.Expect(fixture.Page.Locator("#filterUser")).ToHaveValueAsync("");
        fixture.AssertNoBrowserErrors();
    }

    private static bool IsActivityResponse(IResponse response, string? actorId)
    {
        var uri = new Uri(response.Url);
        if (response.Request.Method != "POST" || uri.AbsolutePath != "/User/GetActivityLogAjax" || response.Request.PostData is not { } body)
            return false;
        var query = QueryHelpers.ParseQuery(uri.Query);
        using var json = JsonDocument.Parse(body);
        var actualActorId = query.TryGetValue("actorId", out var value) ? value.ToString() : "";
        return actualActorId == (actorId ?? "") && json.RootElement.GetProperty("start").GetInt32() == 0;
    }
}
