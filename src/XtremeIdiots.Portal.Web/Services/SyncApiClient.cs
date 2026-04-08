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

    public Task<SyncTriggerResult> TriggerVerify(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return TriggerOrchestration($"/api/map-rotations/verify/{assignmentId}", cancellationToken);
    }

    public async Task<OrchestrationStatusQueryResult> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenResult = await sharedCredential.GetTokenAsync(
                new TokenRequestContext([applicationAudience + "/.default"]), cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + $"/api/map-rotations/status/{instanceId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.Token);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.NotFound);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Sync API returned {StatusCode} for orchestration status {InstanceId}", (int)response.StatusCode, instanceId);
                return new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Error);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = System.Text.Json.JsonSerializer.Deserialize<OrchestrationStatusResult>(json, jsonOptions);
            return new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found, result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get orchestration status for {InstanceId}", instanceId);
            return new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Error);
        }
    }

    public async Task<SyncTriggerResult> TerminateOrchestration(string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenResult = await sharedCredential.GetTokenAsync(
                new TokenRequestContext([applicationAudience + "/.default"]), cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + $"/api/map-rotations/terminate/{instanceId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.Token);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Successfully terminated orchestration {InstanceId}", instanceId);
                return new SyncTriggerResult(true);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Failed to terminate orchestration {InstanceId}, status {StatusCode}: {ErrorBody}", instanceId, (int)response.StatusCode, errorBody);
            return new SyncTriggerResult(false, Error: $"Sync service returned HTTP {(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate orchestration {InstanceId}", instanceId);
            return new SyncTriggerResult(false, Error: ex.Message);
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
