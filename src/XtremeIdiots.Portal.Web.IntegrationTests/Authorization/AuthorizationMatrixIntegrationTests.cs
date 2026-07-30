using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Authorization;

public class AuthorizationMatrixIntegrationTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;
    private IAuthorizationPolicyProvider policyProvider = null!;
    private IAuthorizationService authorizationService = null!;

    public async Task InitializeAsync()
    {
        host = await PortalWebTestHost.CreateAsync();
        authorizationService = host.Services.GetRequiredService<IAuthorizationService>();
        policyProvider = host.Services.GetRequiredService<IAuthorizationPolicyProvider>();
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public async Task PolicyRoleMatrix_MatchesExpectedOutcomes()
    {
        List<string> mismatches = [];

        foreach (var entry in AuthorizationMatrix.Entries)
        {
            foreach (var role in Enum.GetValues<PortalTestRole>())
            {
                var principal = CreatePrincipal(role);
                var expected = AuthorizationMatrix.IsAllowed(entry, role);
                var result = await authorizationService.AuthorizeAsync(principal, entry.Resource, entry.Policy);

                if (result.Succeeded != expected)
                {
                    mismatches.Add($"{entry.Policy} ({entry.Scenario}) for {role}: expected {expected}, actual {result.Succeeded}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public async Task AssignablePolicies_AcceptMatchingDirectPermissions()
    {
        List<string> failures = [];

        foreach (var entry in AuthorizationMatrix.Entries.Where(entry => entry.DirectPermissionAssignable))
        {
            var principal = CreateDirectPermissionPrincipal(entry.Policy, AuthorizationMatrix.PermissionValueFor(entry.Resource));
            var result = await authorizationService.AuthorizeAsync(principal, entry.Resource, entry.Policy);

            if (!result.Succeeded)
            {
                failures.Add($"{entry.Policy} ({entry.Scenario}) rejected its matching direct permission");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task GameScopedDirectPermissions_RejectMismatchedGameValues()
    {
        List<string> failures = [];

        foreach (var entry in AuthorizationMatrix.Entries
                     .Where(entry => entry.DirectPermissionAssignable && AuthorizationMatrix.TryGetResourceGameType(entry.Resource, out _)))
        {
            var principal = CreateDirectPermissionPrincipal(entry.Policy, GameType.Insurgency.ToString());
            var result = await authorizationService.AuthorizeAsync(principal, entry.Resource, entry.Policy);

            if (result.Succeeded)
            {
                failures.Add($"{entry.Policy} ({entry.Scenario}) accepted a mismatched game-scoped direct permission");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Cod4RoleClaims_AuthorizeEquivalentCod4xResourcesButNotOtherGames()
    {
        var gameAdmin = CreatePrincipal(PortalTestRole.GameAdmin);

        var equivalentResult = await authorizationService.AuthorizeAsync(
            gameAdmin,
            GameType.CallOfDuty4x,
            AuthPolicies.MapRotations_Write);
        var differentGameResult = await authorizationService.AuthorizeAsync(
            gameAdmin,
            GameType.Insurgency,
            AuthPolicies.MapRotations_Write);

        Assert.True(equivalentResult.Succeeded);
        Assert.False(differentGameResult.Succeeded);
    }

    [Fact]
    public async Task PotentialAccessMatrix_MatchesExpectedOutcomes()
    {
        List<string> mismatches = [];

        foreach (var entry in AuthorizationMatrix.PotentialAccessEntries)
        {
            foreach (var role in Enum.GetValues<PortalTestRole>())
            {
                var principal = CreatePrincipal(role);
                var expected = AuthorizationMatrix.IsAllowed(entry, role);
                var result = await authorizationService.AuthorizeAsync(principal, entry.Resource, entry.Policy);

                if (result.Succeeded != expected)
                {
                    mismatches.Add($"{entry.Policy} potential access for {role}: expected {expected}, actual {result.Succeeded}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public async Task EveryPolicyConstant_IsRegisteredOnceAndRepresentedInMatrix()
    {
        var policyConstants = typeof(AuthPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var matrixPolicies = AuthorizationMatrix.Entries
            .Select(entry => entry.Policy)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(policyConstants, matrixPolicies);

        foreach (var policyName in policyConstants)
        {
            var policy = await policyProvider.GetPolicyAsync(policyName);
            Assert.NotNull(policy);
            Assert.Single(policy.Requirements);
        }
    }

    [Fact]
    public void MatrixEntries_AreUniqueAndDefineFiveBaselineRoles()
    {
        var duplicateKeys = AuthorizationMatrix.Entries
            .GroupBy(entry => (entry.Policy, entry.Scenario))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Policy} ({group.Key.Scenario})")
            .ToArray();

        Assert.Empty(duplicateKeys);
        Assert.Equal(5, Enum.GetValues<PortalTestRole>().Length);
    }

    [Fact]
    public void NonAssignablePolicies_AreExplicitAndHaveNoDirectPermissionCases()
    {
        var nonAssignableFromEntries = AuthorizationMatrix.Entries
            .Where(entry => !entry.DirectPermissionAssignable)
            .Select(entry => entry.Policy)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = AuthorizationMatrix.NonAssignablePolicies.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, nonAssignableFromEntries);
    }

    private static ClaimsPrincipal CreateDirectPermissionPrincipal(string policy, string value)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "direct-permission-user"),
            new Claim(policy, value),
        ], TestAuthenticationDefaults.Scheme);

        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreatePrincipal(PortalTestRole role)
    {
        return role switch
        {
            PortalTestRole.Anonymous => new ClaimsPrincipal(new ClaimsIdentity()),
            PortalTestRole.Moderator => TestPrincipalProfiles.Create(TestPrincipalProfiles.Moderator),
            PortalTestRole.GameAdmin => TestPrincipalProfiles.Create(TestPrincipalProfiles.GameAdmin),
            PortalTestRole.HeadAdmin => TestPrincipalProfiles.Create(TestPrincipalProfiles.HeadAdmin),
            PortalTestRole.SeniorAdmin => TestPrincipalProfiles.Create(TestPrincipalProfiles.SeniorAdmin),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }
}
