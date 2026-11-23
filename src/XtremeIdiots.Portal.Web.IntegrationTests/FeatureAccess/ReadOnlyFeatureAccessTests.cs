using Microsoft.Extensions.DependencyInjection;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

/// <summary>
/// End-to-end authorization boundary tests for read-only web feature landing pages.
/// For every feature in <see cref="ReadOnlyFeatureCatalog"/> and every baseline role this asserts:
/// <list type="bullet">
/// <item>an authenticated but unauthorized role receives HTTP 403;</item>
/// <item>an anonymous visitor to a protected page is redirected to the login page;</item>
/// <item>an authorized role (or anonymous on a public page) is neither forbidden nor bounced to login.</item>
/// </list>
/// This complements <see cref="AuthorizationMatrixIntegrationTests"/> (handler level) by validating
/// the endpoint-to-policy wiring over real HTTP.
/// </summary>
[Trait("Category", "HttpIntegration")]
public sealed class ReadOnlyFeatureAccessTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;

    public async Task InitializeAsync()
    {
        host = await PortalWebTestHost.CreateAsync().ConfigureAwait(false);
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([]))));
        var profileId = Guid.NewGuid();
        var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<UserProfileDto>($"{{\"UserProfileId\":\"{profileId}\"}}")!;
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));
        repository.Setup(r => r.ConnectedPlayers.V1.GetActiveConnectedPlayerActivationCode(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConnectedPlayerActivationCodeDto>(HttpStatusCode.NotFound));
        repository.Setup(r => r.ConnectedPlayers.V1.GetConnectedPlayersByUserProfile(profileId, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConnectedPlayerDto>>(new CollectionModel<ConnectedPlayerDto>([]))));
        repository.Setup(r => r.ConnectedPlayers.V1.GetPendingConnectedPlayerClaims(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerClaimDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConnectedPlayerClaimDto>>(new CollectionModel<ConnectedPlayerClaimDto>([]))));
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Every_read_only_feature_enforces_the_expected_authorization_boundary()
    {
        var mismatches = new List<string>();

        foreach (var feature in ReadOnlyFeatureCatalog.Features)
        {
            foreach (var role in TestRoles.Baseline)
            {
                var response = await SendAsync(feature.Route, role);
                var allowed = ReadOnlyFeatureCatalog.IsAllowed(feature, role);
                var isLoginRedirect = IsLoginRedirect(response);

                if (!allowed)
                {
                    if (role == PortalTestRole.Anonymous)
                    {
                        if (!isLoginRedirect)
                        {
                            mismatches.Add($"{feature.Name} ({feature.Route}) [{role}]: expected login redirect, got {Describe(response)}.");
                        }
                    }
                    else if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        mismatches.Add($"{feature.Name} ({feature.Route}) [{role}]: expected 403 Forbidden, got {Describe(response)}.");
                    }

                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    mismatches.Add($"{feature.Name} ({feature.Route}) [{role}]: expected access, got 403 Forbidden.");
                }
                else if (isLoginRedirect)
                {
                    mismatches.Add($"{feature.Name} ({feature.Route}) [{role}]: expected access, got login redirect.");
                }
            }
        }

        Assert.True(mismatches.Count == 0, "Read-only feature access boundary mismatches:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public async Task Renderable_read_only_features_return_200_for_authorized_roles()
    {
        var failures = new List<string>();

        foreach (var feature in ReadOnlyFeatureCatalog.Features.Where(f => f.RendersUnderMock))
        {
            foreach (var role in TestRoles.Baseline)
            {
                if (!ReadOnlyFeatureCatalog.IsAllowed(feature, role))
                {
                    continue;
                }

                var response = await SendAsync(feature.Route, role);

                if (response.StatusCode != HttpStatusCode.OK && !IsAuthorizedRedirect(response, feature))
                {
                    var expected = feature.AuthorizedRedirectPath is null
                        ? "200 OK"
                        : $"200 OK or redirect to {feature.AuthorizedRedirectPath}";
                    failures.Add($"{feature.Name} ({feature.Route}) [{role}]: expected {expected}, got {Describe(response)}.");
                }
            }
        }

        Assert.True(failures.Count == 0, "Renderable read-only feature render failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private async Task<HttpResponseMessage> SendAsync(string route, PortalTestRole role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);

        var profile = TestRoles.ProfileFor(role);
        if (profile is not null)
        {
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        }

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static bool IsLoginRedirect(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.MovedPermanently))
        {
            return false;
        }

        var location = response.Headers.Location?.ToString();
        return location is not null && location.Contains("Login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizedRedirect(HttpResponseMessage response, ReadOnlyFeature feature)
    {
        return feature.AuthorizedRedirectPath is not null
            && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found
            && string.Equals(
                response.Headers.Location?.ToString(),
                feature.AuthorizedRedirectPath,
                StringComparison.Ordinal);
    }

    private static string Describe(HttpResponseMessage response)
    {
        var status = $"{(int)response.StatusCode} {response.StatusCode}";
        var location = response.Headers.Location?.ToString();
        return location is null ? status : $"{status} -> {location}";
    }
}
