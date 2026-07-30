using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

public class PortalActionManifestCoverageTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;

    public async Task InitializeAsync()
    {
        host = await PortalWebTestHost.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public void DiscoveredActions_MatchApprovedManifest()
    {
        var discovered = PortalActionManifest.Discover(host.Services);
        var actualLines = discovered.Select(entry => entry.SnapshotLine).ToArray();
        var actualPath = Path.Combine(AppContext.BaseDirectory, "portal-actions.actual.txt");
        var normalizedManifest = string.Join('\n', actualLines) + '\n';
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedManifest)));

        if (!string.Equals(fingerprint, PortalActionManifest.ApprovedFingerprint, StringComparison.Ordinal))
        {
            File.WriteAllLines(actualPath, actualLines);
        }

        Assert.Equal(PortalActionManifest.ApprovedFingerprint, fingerprint);
    }

    [Fact]
    public void DiscoveredActions_HaveUniqueKeysAndKnownClassifications()
    {
        var discovered = PortalActionManifest.Discover(host.Services);
        var duplicateKeys = discovered
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.NotEmpty(discovered);
        Assert.Empty(duplicateKeys);
        Assert.All(discovered, entry => Assert.True(Enum.IsDefined(entry.Kind)));

        var actualCounts = discovered
            .GroupBy(entry => entry.Kind)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(PortalActionManifest.ApprovedCounts, actualCounts);
    }

    [Fact]
    public async Task ControllerAndActionPolicyReferences_ResolveToRegisteredPolicies()
    {
        var descriptorProvider = host.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var policyProvider = host.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policyNames = descriptorProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerTypeInfo.Assembly == typeof(PortalWebApplication).Assembly)
            .SelectMany(descriptor => descriptor.EndpointMetadata.OfType<IAuthorizeData>())
            .Select(authorizeData => authorizeData.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        List<string> missingPolicies = [];

        foreach (var policyName in policyNames)
        {
            if (await policyProvider.GetPolicyAsync(policyName!) is null)
            {
                missingPolicies.Add(policyName!);
            }
        }

        Assert.NotEmpty(policyNames);
        Assert.Empty(missingPolicies);
    }
}
