using System.Net;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

public sealed partial class UserManageProfileAccessTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;
    private UserManageProfileScenario scenario = null!;

    public async Task InitializeAsync()
    {
        scenario = new UserManageProfileScenario();
        host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public async Task ManageProfile_route_is_reachable_for_cod5_head_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdminCod5);

        var response = await host.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Manage User Profile - Route Test User", content, StringComparison.Ordinal);
        Assert.Contains("route-test@example.invalid", content, StringComparison.Ordinal);
        Assert.Contains("Route Notification", content, StringComparison.Ordinal);
        Assert.Contains("Route notification message", content, StringComparison.Ordinal);
        Assert.Contains("Unsupported Notification", content, StringComparison.Ordinal);
        Assert.Contains(">N/A<", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManageProfile_route_remains_forbidden_for_game_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameAdmin);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManageProfile_route_remains_forbidden_for_moderator()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManageNotifications_route_redirects_for_cod5_head_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageNotifications/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdminCod5);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/User/ManageProfile/{scenario.UserProfileId}?tab=notifications#notifications", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_post_binds_checked_and_unchecked_channels_from_manage_profile_form()
    {
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        getRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        var getResponse = await host.Client.SendAsync(getRequest);
        var content = await getResponse.Content.ReadAsStringAsync();
        var tokenMatch = RequestVerificationTokenRegex().Match(content);
        var cookieHeader = string.Join(
            "; ",
            getResponse.Headers.TryGetValues("Set-Cookie", out var setCookieValues)
                ? setCookieValues.Select(static value => value.Split(';', 2)[0])
                : []);

        Assert.True(tokenMatch.Success);
        Assert.NotEmpty(cookieHeader);

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/User/UpdateUserNotificationPreferences")
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Id", scenario.UserProfileId.ToString()),
                new KeyValuePair<string, string>("Preferences[0].NotificationTypeId", scenario.NotificationTypeId.ToString()),
                new KeyValuePair<string, string>("Preferences[0].EmailEnabled", "false"),
                new KeyValuePair<string, string>("Preferences[0].EmailEnabled", "true"),
                new KeyValuePair<string, string>("Preferences[0].InAppEnabled", "false"),
                new KeyValuePair<string, string>("Preferences[0].InAppEnabled", "true"),
                new KeyValuePair<string, string>("Preferences[1].NotificationTypeId", scenario.UnsupportedNotificationTypeId.ToString()),
                new KeyValuePair<string, string>("Preferences[1].EmailEnabled", "false"),
                new KeyValuePair<string, string>("Preferences[1].InAppEnabled", "false"),
                new KeyValuePair<string, string>("__RequestVerificationToken", tokenMatch.Groups[1].Value)
            ])
        };
        postRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        postRequest.Headers.Add("Cookie", cookieHeader);

        var postResponse = await host.Client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.Equal($"/User/ManageProfile/{scenario.UserProfileId}?tab=notifications#notifications", postResponse.Headers.Location?.ToString());
        Assert.Equal(1, scenario.UpdateNotificationPreferencesCallCount);

        Assert.Equal(2, scenario.UpdatedPreferences.Count);

        var updatedPreference = Assert.Single(
            scenario.UpdatedPreferences,
            preference => preference.NotificationTypeId == scenario.NotificationTypeId.ToString());
        Assert.True(updatedPreference.EmailEnabled);
        Assert.True(updatedPreference.InSiteEnabled);

        var unsupportedPreference = Assert.Single(
            scenario.UpdatedPreferences,
            preference => preference.NotificationTypeId == scenario.UnsupportedNotificationTypeId.ToString());
        Assert.False(unsupportedPreference.EmailEnabled);
        Assert.False(unsupportedPreference.InSiteEnabled);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RequestVerificationTokenRegex();
}
