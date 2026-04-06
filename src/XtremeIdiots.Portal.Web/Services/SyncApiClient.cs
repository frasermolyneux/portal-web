using Azure.Core;
using Azure.Identity;

namespace XtremeIdiots.Portal.Web.Services;

public class SyncApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<SyncApiClient> logger) : ISyncApiClient
{
    private readonly static TokenCredential sharedCredential = new DefaultAzureCredential();
    private readonly static System.Text.Json.JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string baseUrl = configuration["SyncApi:BaseUrl"] ?? throw new InvalidOperationException("SyncApi:BaseUrl is required");
    private readonly string applicationAudience = (configuration["SyncApi:ApplicationAudience"] ?? throw new InvalidOperationException("SyncApi:ApplicationAudience is required")).TrimEnd('/');

    public Task<SyncTriggerResult> TriggerSync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return TriggerOrchestration($"/api/map-rotations/sync/{assignmentId}", cancellationToken);
    }

    public Task<SyncTriggerResult> TriggerActivate(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return TriggerOrchestration($"/api/map-rotations/activate/{assignmentId}", cancellationToken);
    }

    public Task<SyncTriggerResult> TriggerDeactivate(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return TriggerOrchestration($"/api/map-rotations/deactivate/{assignmentId}", cancellationToken);
    }

    public Task<SyncTriggerResult> TriggerRemove(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return TriggerOrchestration($"/api/map-rotations/remove/{assignmentId}", cancellationToken);
    }

    public async Task<OrchestrationStatusResult?> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenResult = await sharedCredential.GetTokenAsync(
                new TokenRequestContext([applicationAudience + "/.default"]), cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + $"/api/map-rotations/status/{instanceId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.Token);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<OrchestrationStatusResult>(json, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get orchestration status for {InstanceId}", instanceId);
            return null;
        }
    }

    private async Task<SyncTriggerResult> TriggerOrchestration(string path, CancellationToken cancellationToken)
    {
        try
        {
            var tokenResult = await sharedCredential.GetTokenAsync(
                new TokenRequestContext([applicationAudience + "/.default"]), cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.Token);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new SyncTriggerResult(true);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogError("Sync API returned {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);
            return new SyncTriggerResult(false, Error: "The sync service returned an error. Please try again or contact an administrator.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call sync API at {Path}", path);
            return new SyncTriggerResult(false, Error: "The sync service is currently unavailable. Please try again later.");
        }
    }
}
