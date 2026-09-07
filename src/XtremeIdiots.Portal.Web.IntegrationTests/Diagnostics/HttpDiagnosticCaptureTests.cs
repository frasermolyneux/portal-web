using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "HttpIntegration")]
public sealed class HttpDiagnosticCaptureTests
{
    [Fact]
    public async Task TestServerRequests_AreCorrelatedWithTheCurrentDiagnosticScope()
    {
        var root = Path.Combine(Path.GetTempPath(), $"portal-http-diagnostics-{Guid.NewGuid():N}");
        var scope = new TestDiagnosticScope("HTTP request", GetType().FullName!, nameof(TestServerRequests_AreCorrelatedWithTheCurrentDiagnosticScope), root);
        using var activation = TestDiagnosticScope.Activate(scope);
        try
        {
            await using var host = await PortalWebTestHost.CreateAsync();
            using var response = await host.Client.GetAsync("/api/health/live");
            response.EnsureSuccessStatusCode();
            scope.SaveFailure("Failed");

            var logs = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "application.log"));
            Assert.Contains("Request GET /api/health/live", logs, StringComparison.Ordinal);
            Assert.Contains("Response 200 GET /api/health/live", logs, StringComparison.Ordinal);
        }
        finally
        {
            scope.Complete("Passed");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
