using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Server.Events.Processor.App.Commands;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages fleet-wide global configuration defaults for agents, ban files, moderation, and events
/// </summary>
[Authorize(Policy = AuthPolicies.GlobalSettings_Admin)]
public class GlobalSettingsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<GlobalSettingsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
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

            model.AgentName = NormalizeAgentName(model.AgentName);

            var errors = new List<string>();

            await UpsertConfigSafeAsync("agent", JsonSerializer.Serialize(new
            {
                pollIntervalMs = model.AgentPollIntervalMs,
                statusPublishIntervalSeconds = model.AgentStatusPublishIntervalSeconds,
                rconSyncIntervalSeconds = model.AgentRconSyncIntervalSeconds,
                offsetSaveIntervalSeconds = model.AgentOffsetSaveIntervalSeconds,
                agentName = model.AgentName
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("banfiles", JsonSerializer.Serialize(new
            {
                checkIntervalSeconds = model.BanFileSyncCheckIntervalSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("moderation", JsonSerializer.Serialize(new
            {
                contentSafetyHateSeverityThreshold = model.ModerationHateSeverityThreshold,
                contentSafetyViolenceSeverityThreshold = model.ModerationViolenceSeverityThreshold,
                contentSafetySexualSeverityThreshold = model.ModerationSexualSeverityThreshold,
                contentSafetySelfHarmSeverityThreshold = model.ModerationSelfHarmSeverityThreshold,
                minMessageLength = model.ModerationMinMessageLength
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync("events", JsonSerializer.Serialize(new
            {
                staleThresholdSeconds = model.EventsStaleThresholdSeconds,
                playerCacheExpirationSeconds = model.EventsPlayerCacheExpirationSeconds
            }, configJsonOptions), errors, cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync(
                ChatCommandSettingsConstants.Namespace,
                ChatCommandSettingsJsonMapper.BuildGlobalConfigurationJson(model.ChatCommands),
                errors,
                cancellationToken).ConfigureAwait(false);

            await UpsertConfigSafeAsync(
                WelcomeMessageSettingsViewModelConstants.Namespace,
                WelcomeMessageSettingsJsonMapper.BuildGlobalConfigurationJson(model.WelcomeMessages),
                errors,
                cancellationToken).ConfigureAwait(false);

            if (errors.Count > 0)
            {
                this.AddAlertDanger($"Failed to save configuration for: {string.Join(", ", errors)}");
            }
            else
            {
                this.AddAlertSuccess("Global settings saved successfully.");
                TrackSuccessTelemetry("GlobalSettingsUpdated", nameof(Index));
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
                    model.AgentName = NormalizeAgentName(GetStringProperty(root, "agentName"));
                    break;
                case "banfiles":
                    model.BanFileSyncCheckIntervalSeconds = GetIntProperty(root, "checkIntervalSeconds", model.BanFileSyncCheckIntervalSeconds);
                    break;
                case "moderation":
                    var legacyThreshold = GetNullableIntProperty(root, "contentSafetySeverityThreshold");
                    model.ModerationHateSeverityThreshold = GetIntProperty(root, "contentSafetyHateSeverityThreshold", legacyThreshold ?? model.ModerationHateSeverityThreshold);
                    model.ModerationViolenceSeverityThreshold = GetIntProperty(root, "contentSafetyViolenceSeverityThreshold", legacyThreshold ?? model.ModerationViolenceSeverityThreshold);
                    model.ModerationSexualSeverityThreshold = GetIntProperty(root, "contentSafetySexualSeverityThreshold", legacyThreshold ?? model.ModerationSexualSeverityThreshold);
                    model.ModerationSelfHarmSeverityThreshold = GetIntProperty(root, "contentSafetySelfHarmSeverityThreshold", legacyThreshold ?? model.ModerationSelfHarmSeverityThreshold);
                    model.ModerationMinMessageLength = GetIntProperty(root, "minMessageLength", model.ModerationMinMessageLength);
                    break;
                case "events":
                    model.EventsStaleThresholdSeconds = GetIntProperty(root, "staleThresholdSeconds", model.EventsStaleThresholdSeconds);
                    model.EventsPlayerCacheExpirationSeconds = GetIntProperty(root, "playerCacheExpirationSeconds", model.EventsPlayerCacheExpirationSeconds);
                    break;
                case ChatCommandSettingsConstants.Namespace:
                    ChatCommandSettingsJsonMapper.PopulateGlobal(model.ChatCommands, root);
                    break;
                case WelcomeMessageSettingsViewModelConstants.Namespace:
                    WelcomeMessageSettingsJsonMapper.PopulateGlobal(model.WelcomeMessages, root);
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

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }

    private static int? GetNullableIntProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.Number &&
               prop.TryGetInt32(out var value)
            ? value
            : null;
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
