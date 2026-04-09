using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages fleet-wide global configuration defaults for agents, ban files, moderation, and events
/// </summary>
[Authorize(Policy = AuthPolicies.AccessGlobalSettings)]
public class GlobalSettingsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<GlobalSettingsController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    private readonly static JsonSerializerOptions configJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var model = new GlobalSettingsViewModel();

            try
            {
                var configsResult = await repositoryApiClient.GlobalConfigurations.V1
                    .GetConfigurations(cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        PopulateModelFromNamespace(model, config);
                    }
                }
                else
                {
                    Logger.LogWarning("Failed to retrieve global configurations, using defaults");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch global configurations, using defaults");
            }

            return View(model);
        }, nameof(Index)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(GlobalSettingsViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var modelStateResult = CheckModelState(model);
            if (modelStateResult is not null)
                return modelStateResult;

            var errors = new List<string>();

            await UpsertConfigSafeAsync("agent", JsonSerializer.Serialize(new
            {
                pollIntervalMs = model.AgentPollIntervalMs,
                statusPublishIntervalSeconds = model.AgentStatusPublishIntervalSeconds,
                rconSyncIntervalSeconds = model.AgentRconSyncIntervalSeconds,
                offsetSaveIntervalSeconds = model.AgentOffsetSaveIntervalSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("banfiles", JsonSerializer.Serialize(new
            {
                checkIntervalSeconds = model.BanFileSyncCheckIntervalSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("moderation", JsonSerializer.Serialize(new
            {
                contentSafetySeverityThreshold = model.ModerationSeverityThreshold,
                minMessageLength = model.ModerationMinMessageLength
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("events", JsonSerializer.Serialize(new
            {
                staleThresholdSeconds = model.EventsStaleThresholdSeconds,
                playerCacheExpirationSeconds = model.EventsPlayerCacheExpirationSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            if (errors.Count > 0)
            {
                this.AddAlertDanger($"Failed to save configuration for: {string.Join(", ", errors)}");
            }
            else
            {
                this.AddAlertSuccess("Global settings saved successfully.");
            }

            return RedirectToAction(nameof(Index));
        }, nameof(Index)).ConfigureAwait(false);
    }

    private void PopulateModelFromNamespace(GlobalSettingsViewModel model, ConfigurationDto config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
                return;

            using var doc = JsonDocument.Parse(config.Configuration);
            var root = doc.RootElement;

            switch (config.Namespace)
            {
                case "agent":
                    model.AgentPollIntervalMs = GetIntProperty(root, "pollIntervalMs", model.AgentPollIntervalMs);
                    model.AgentStatusPublishIntervalSeconds = GetIntProperty(root, "statusPublishIntervalSeconds", model.AgentStatusPublishIntervalSeconds);
                    model.AgentRconSyncIntervalSeconds = GetIntProperty(root, "rconSyncIntervalSeconds", model.AgentRconSyncIntervalSeconds);
                    model.AgentOffsetSaveIntervalSeconds = GetIntProperty(root, "offsetSaveIntervalSeconds", model.AgentOffsetSaveIntervalSeconds);
                    break;
                case "banfiles":
                    model.BanFileSyncCheckIntervalSeconds = GetIntProperty(root, "checkIntervalSeconds", model.BanFileSyncCheckIntervalSeconds);
                    break;
                case "moderation":
                    model.ModerationSeverityThreshold = GetIntProperty(root, "contentSafetySeverityThreshold", model.ModerationSeverityThreshold);
                    model.ModerationMinMessageLength = GetIntProperty(root, "minMessageLength", model.ModerationMinMessageLength);
                    break;
                case "events":
                    model.EventsStaleThresholdSeconds = GetIntProperty(root, "staleThresholdSeconds", model.EventsStaleThresholdSeconds);
                    model.EventsPlayerCacheExpirationSeconds = GetIntProperty(root, "playerCacheExpirationSeconds", model.EventsPlayerCacheExpirationSeconds);
                    break;
                default:
                    Logger.LogDebug("Unknown global configuration namespace '{Namespace}'", config.Namespace);
                    break;
            }
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse global configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.Number &&
               prop.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private async Task UpsertConfigSafeAsync(
        string ns,
        string configJson,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryApiClient.GlobalConfigurations.V1.UpsertConfiguration(
                ns, new UpsertConfigurationDto { Configuration = configJson }, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("Failed to upsert global configuration namespace '{Namespace}'", ns);
                errors.Add(ns);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error upserting global configuration namespace '{Namespace}'", ns);
            errors.Add(ns);
        }
    }
}
