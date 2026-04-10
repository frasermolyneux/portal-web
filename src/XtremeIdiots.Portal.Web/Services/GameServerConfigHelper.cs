using System.Text.Json;

using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Services;

/// <summary>
/// Provides helper methods for fetching and reading game server configuration namespaces
/// </summary>
public static class GameServerConfigHelper
{
    /// <summary>
    /// Fetches configuration namespaces for multiple game servers and returns a lookup dictionary
    /// </summary>
    /// <param name="repositoryApiClient">The repository API client</param>
    /// <param name="serverIds">The game server IDs to fetch configurations for</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping server ID → namespace → parsed JSON root element</returns>
    public async static Task<Dictionary<Guid, Dictionary<string, JsonElement>>> FetchConfigsForServersAsync(
        IRepositoryApiClient repositoryApiClient,
        IEnumerable<Guid> serverIds,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, Dictionary<string, JsonElement>>();

        foreach (var id in serverIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var configsResult = await repositoryApiClient.GameServerConfigurations.V1
                    .GetConfigurations(id, cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    var serverConfigs = new Dictionary<string, JsonElement>();

                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        if (string.IsNullOrWhiteSpace(config.Configuration))
                            continue;

                        try
                        {
                            var doc = JsonDocument.Parse(config.Configuration);
                            serverConfigs[config.Namespace] = doc.RootElement.Clone();
                        }
                        catch (JsonException ex)
                        {
                            logger?.LogWarning(ex, "Failed to parse configuration JSON for server {ServerId} namespace {Namespace}",
                                id, config.Namespace);
                        }
                    }

                    result[id] = serverConfigs;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to fetch configurations for server {ServerId}", id);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets a string configuration value from the server configs lookup
    /// </summary>
    public static string GetConfigValue(
        Dictionary<Guid, Dictionary<string, JsonElement>>? configs,
        Guid serverId, string ns, string property)
    {
        if (configs != null &&
            configs.TryGetValue(serverId, out var nsConfigs) &&
            nsConfigs.TryGetValue(ns, out var root) &&
            root.TryGetProperty(property, out var val) &&
            val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// Gets an integer configuration value from the server configs lookup
    /// </summary>
    public static int GetConfigIntValue(
        Dictionary<Guid, Dictionary<string, JsonElement>>? configs,
        Guid serverId, string ns, string property, int defaultValue = 0)
    {
        if (configs != null &&
            configs.TryGetValue(serverId, out var nsConfigs) &&
            nsConfigs.TryGetValue(ns, out var root) &&
            root.TryGetProperty(property, out var val) &&
            val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return defaultValue;
    }
}
