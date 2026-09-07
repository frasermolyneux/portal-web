using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

[Trait("Category", "HttpIntegration")]
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
        var approvedLines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Manifest", "portal-actions.approved.txt"));
        var difference = ManifestSnapshotDiff.Compare(approvedLines, actualLines);
        if (difference.Length == 0)
            return;

        var root = Environment.GetEnvironmentVariable("PORTAL_TEST_DIAGNOSTICS_DIRECTORY");
        var artifactDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(root) ? Path.Combine(AppContext.BaseDirectory, "TestResults", "diagnostics") : root,
            $"manifest-{Guid.NewGuid():N}");
        string artifactMessage;
        try
        {
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllLines(Path.Combine(artifactDirectory, "portal-actions.approved.txt"), approvedLines);
            File.WriteAllLines(Path.Combine(artifactDirectory, "portal-actions.actual.txt"), actualLines);
            File.WriteAllText(Path.Combine(artifactDirectory, "portal-actions.diff.txt"), difference);
            artifactMessage = $"Manifest artifacts: {artifactDirectory}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            artifactMessage = $"Could not write manifest artifacts to {artifactDirectory}: {exception.Message}";
        }

        Assert.Fail($"{difference}{Environment.NewLine}{artifactMessage}");
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
