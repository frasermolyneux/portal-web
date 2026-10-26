using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using MX.InvisionCommunity.Api.Abstractions;
using MX.InvisionCommunity.Api.Abstractions.Models;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed partial class UserArchiveAccessTests : IAsyncLifetime
{
    private readonly Guid profileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IInvisionApiClient> forums = new() { DefaultValue = DefaultValue.Mock };
    private PortalWebTestHost host = null!;

    public async Task InitializeAsync()
    {
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = profileId,
            XtremeIdiotsForumId = "42",
            DisplayName = "Former Forum User",
            UserProfileClaims = Array.Empty<object>()
        }));
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileIncludingArchived(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));
        repository.Setup(client => client.UserProfiles.V1.ArchiveUserProfile(
            It.IsAny<ArchiveUserProfileDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
        forums.Setup(client => client.Core.GetMember("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MemberDto>(HttpStatusCode.NotFound));
        host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
            services.RemoveAll<IInvisionApiClient>();
            services.AddSingleton(forums.Object);
        });
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Archive_confirmation_posts_with_antiforgery_and_resolved_actor()
    {
        string? originalStamp;
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var identityUser = new IdentityUser { Id = "42", UserName = "former-user", Email = "former@example.invalid" };
            Assert.True((await users.CreateAsync(identityUser)).Succeeded);
            originalStamp = identityUser.SecurityStamp;
        }

        using var get = CreateRequest(HttpMethod.Get, TestPrincipalProfiles.SeniorAdmin);
        var confirmation = await host.Client.SendAsync(get);
        var html = await confirmation.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Contains("Historical actions, demos and other attribution are preserved.", html, StringComparison.Ordinal);
        var token = RequestVerificationTokenRegex().Match(html);
        Assert.True(token.Success);
        var cookies = string.Join("; ", confirmation.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        using var post = CreateRequest(HttpMethod.Post, TestPrincipalProfiles.SeniorAdmin);
        post.Headers.Add("Cookie", cookies);
        post.Content = new FormUrlEncodedContent(
        [
            new("Id", profileId.ToString()),
            new("Reason", "Forum account deleted."),
            new("__RequestVerificationToken", token.Groups[1].Value)
        ]);

        var result = await host.Client.SendAsync(post);

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/User", result.Headers.Location?.ToString());
        repository.Verify(client => client.UserProfiles.V1.ArchiveUserProfile(
            It.Is<ArchiveUserProfileDto>(dto => dto.UserProfileId == profileId &&
                dto.ArchivedByUserProfileId == Guid.Parse("11111111-1111-1111-1111-111111111111")),
            It.IsAny<CancellationToken>()), Times.Once);
        using var verificationScope = host.Services.CreateScope();
        var identityUsers = verificationScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var archivedIdentity = await identityUsers.FindByIdAsync("42");
        Assert.NotNull(archivedIdentity);
        Assert.True(await identityUsers.IsLockedOutAsync(archivedIdentity));
        Assert.NotEqual(originalStamp, archivedIdentity.SecurityStamp);
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.HeadAdmin, HttpStatusCode.Redirect)]
    [InlineData(TestPrincipalProfiles.GameAdmin, HttpStatusCode.Forbidden)]
    public async Task Archive_confirmation_denies_lower_roles(string role, HttpStatusCode expected)
    {
        using var request = CreateRequest(HttpMethod.Get, role);

        var result = await host.Client.SendAsync(request);

        Assert.Equal(expected, result.StatusCode);
        if (expected == HttpStatusCode.Redirect)
            Assert.Equal("/Errors/Display/401", result.Headers.Location?.ToString());
        forums.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Archive_post_without_antiforgery_cannot_mutate()
    {
        using var request = CreateRequest(HttpMethod.Post, TestPrincipalProfiles.SeniorAdmin);
        request.Content = new FormUrlEncodedContent([new("Id", profileId.ToString()), new("Reason", "Deleted.")]);

        var result = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Errors/Display/400", result.Headers.Location?.ToString());
        repository.Verify(client => client.UserProfiles.V1.ArchiveUserProfile(
            It.IsAny<ArchiveUserProfileDto>(), It.IsAny<CancellationToken>()), Times.Never);
        forums.VerifyNoOtherCalls();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string role)
    {
        var request = new HttpRequestMessage(method, $"/User/Archive/{profileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, role);
        return request;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RequestVerificationTokenRegex();
}
