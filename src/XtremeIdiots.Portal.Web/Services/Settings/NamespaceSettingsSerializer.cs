using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class NamespaceSettingsSerializer : INamespaceSettingsSerializer
{
    private readonly static JsonSerializerOptions configJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<(string Namespace, string Configuration)> BuildGlobalSettingsConfigurations(GlobalSettingsViewModel model)
    {
        return
        [
            (
                "agent",
                JsonSerializer.Serialize(new
                {
                    pollIntervalMs = model.AgentPollIntervalMs,
                    statusPublishIntervalSeconds = model.AgentStatusPublishIntervalSeconds,
                    rconSyncIntervalSeconds = model.AgentRconSyncIntervalSeconds,
                    offsetSaveIntervalSeconds = model.AgentOffsetSaveIntervalSeconds,
                    agentName = NormalizeAgentName(model.AgentName)
                }, configJsonOptions)
            ),
            (
                "banfiles",
                JsonSerializer.Serialize(new
                {
                    checkIntervalSeconds = model.BanFileSyncCheckIntervalSeconds
                }, configJsonOptions)
            ),
            (
                "moderation",
                JsonSerializer.Serialize(new
                {
                    contentSafetyHateSeverityThreshold = model.ModerationHateSeverityThreshold,
                    contentSafetyViolenceSeverityThreshold = model.ModerationViolenceSeverityThreshold,
                    contentSafetySexualSeverityThreshold = model.ModerationSexualSeverityThreshold,
                    contentSafetySelfHarmSeverityThreshold = model.ModerationSelfHarmSeverityThreshold,
                    minMessageLength = model.ModerationMinMessageLength
                }, configJsonOptions)
            ),
            (
                "events",
                JsonSerializer.Serialize(new
                {
                    staleThresholdSeconds = model.EventsStaleThresholdSeconds,
                    playerCacheExpirationSeconds = model.EventsPlayerCacheExpirationSeconds
                }, configJsonOptions)
            ),
            (
                ChatCommandSettingsConstants.Namespace,
                ChatCommandSettingsJsonMapper.BuildGlobalConfigurationJson(model.ChatCommands)
            ),
            (
                WelcomeMessageSettingsViewModelConstants.Namespace,
                WelcomeMessageSettingsJsonMapper.BuildGlobalConfigurationJson(model.WelcomeMessages)
            )
        ];
    }

    public IReadOnlyList<(string Namespace, string Configuration)> BuildGameServerConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots)
    {
        var configurations = new List<(string Namespace, string Configuration)>();
        var activeTransportNamespace = GameServerEditViewModel.GetFileTransportNamespace(model.GameServer.FileTransportType);

        if (canEditFileTransport)
        {
            var fileTransportConfig = new Dictionary<string, object?>
            {
                ["hostname"] = model.FileTransportConfigHostname,
                ["port"] = model.FileTransportConfigPort,
                ["username"] = model.FileTransportConfigUsername,
                ["password"] = model.FileTransportConfigPassword,
                ["mapsRootPath"] = string.IsNullOrWhiteSpace(model.FileTransportConfigMapsRootPath)
                    ? null
                    : model.FileTransportConfigMapsRootPath
            };

            if (string.Equals(activeTransportNamespace, "sftp", StringComparison.OrdinalIgnoreCase))
            {
                fileTransportConfig["hostKeyFingerprint"] = model.FileTransportConfigHostKeyFingerprint;
            }

            configurations.Add((activeTransportNamespace, JsonSerializer.Serialize(fileTransportConfig, configJsonOptions)));
        }

        if (canEditRcon)
        {
            configurations.Add((
                "rcon",
                JsonSerializer.Serialize(new
                {
                    password = model.RconConfigPassword
                }, configJsonOptions)));
        }

        if (model.GameServer.AgentEnabled)
        {
            configurations.Add((
                "agent",
                JsonSerializer.Serialize(new
                {
                    logFilePath = model.AgentConfigLogFilePath,
                    rconSyncEnabled = model.AgentConfigRconSyncEnabled,
                    agentName = string.IsNullOrWhiteSpace(model.AgentConfigName)
                        ? null
                        : model.AgentConfigName
                }, configJsonOptions)));

            if (canConfigureScreenshots)
            {
                configurations.Add((
                    "screenshots",
                    JsonSerializer.Serialize(new
                    {
                        enabled = model.ScreenshotConfigEnabled,
                        directoryPath = model.ScreenshotConfigEnabled ? model.ScreenshotConfigDirectoryPath : null,
                        filePattern = string.IsNullOrWhiteSpace(model.ScreenshotConfigFilePattern)
                            ? GameServerEditViewModel.DefaultScreenshotFilePattern
                            : model.ScreenshotConfigFilePattern.Trim(),
                        pollIntervalSeconds = model.ScreenshotConfigPollIntervalSeconds
                    }, configJsonOptions)));
            }

            var hasModerationOverrides = model.ModerationHateSeverityThreshold.HasValue
                || model.ModerationViolenceSeverityThreshold.HasValue
                || model.ModerationSexualSeverityThreshold.HasValue
                || model.ModerationSelfHarmSeverityThreshold.HasValue
                || model.ModerationMinMessageLength.HasValue
                || !model.ModerationProtectedNameEnforcementEnabled;

            if (hasModerationOverrides)
            {
                var moderationConfig = new Dictionary<string, object?>
                {
                    ["protectedNameEnforcementEnabled"] = model.ModerationProtectedNameEnforcementEnabled
                };

                if (model.ModerationHateSeverityThreshold.HasValue)
                {
                    moderationConfig["contentSafetyHateSeverityThreshold"] = model.ModerationHateSeverityThreshold.Value;
                }

                if (model.ModerationViolenceSeverityThreshold.HasValue)
                {
                    moderationConfig["contentSafetyViolenceSeverityThreshold"] = model.ModerationViolenceSeverityThreshold.Value;
                }

                if (model.ModerationSexualSeverityThreshold.HasValue)
                {
                    moderationConfig["contentSafetySexualSeverityThreshold"] = model.ModerationSexualSeverityThreshold.Value;
                }

                if (model.ModerationSelfHarmSeverityThreshold.HasValue)
                {
                    moderationConfig["contentSafetySelfHarmSeverityThreshold"] = model.ModerationSelfHarmSeverityThreshold.Value;
                }

                if (model.ModerationMinMessageLength.HasValue)
                {
                    moderationConfig["minMessageLength"] = model.ModerationMinMessageLength.Value;
                }

                configurations.Add(("moderation", JsonSerializer.Serialize(moderationConfig, configJsonOptions)));
            }
            else
            {
                configurations.Add((
                    "moderation",
                    JsonSerializer.Serialize(new
                    {
                        protectedNameEnforcementEnabled = model.ModerationProtectedNameEnforcementEnabled
                    }, configJsonOptions)));
            }

            var hasEventsOverrides = model.EventsStaleThresholdSeconds.HasValue
                || model.EventsPlayerCacheExpirationSeconds.HasValue;

            if (hasEventsOverrides)
            {
                var eventsConfig = new Dictionary<string, object?>();

                if (model.EventsStaleThresholdSeconds.HasValue)
                {
                    eventsConfig["staleThresholdSeconds"] = model.EventsStaleThresholdSeconds.Value;
                }

                if (model.EventsPlayerCacheExpirationSeconds.HasValue)
                {
                    eventsConfig["playerCacheExpirationSeconds"] = model.EventsPlayerCacheExpirationSeconds.Value;
                }

                configurations.Add(("events", JsonSerializer.Serialize(eventsConfig, configJsonOptions)));
            }
            else
            {
                configurations.Add(("events", "{}"));
            }

            configurations.Add((
                ChatCommandSettingsConstants.Namespace,
                ChatCommandSettingsJsonMapper.BuildServerConfigurationJson(model.ChatCommands)));

            configurations.Add((
                WelcomeMessageSettingsViewModelConstants.Namespace,
                WelcomeMessageSettingsJsonMapper.BuildServerConfigurationJson(model.WelcomeMessages)));

            var broadcastsIntervalSeconds = model.BroadcastsIntervalSeconds.GetValueOrDefault(GameServerEditViewModel.DefaultBroadcastIntervalSeconds);
            if (broadcastsIntervalSeconds <= 0)
            {
                broadcastsIntervalSeconds = GameServerEditViewModel.DefaultBroadcastIntervalSeconds;
            }

            configurations.Add((
                "broadcasts",
                JsonSerializer.Serialize(new
                {
                    enabled = model.BroadcastsEnabled,
                    intervalSeconds = broadcastsIntervalSeconds,
                    messages = (model.BroadcastMessages ?? []).Select(m => new
                    {
                        message = m.Message,
                        enabled = m.Enabled
                    })
                }, configJsonOptions)));
        }

        if (model.GameServer.BanFileSyncEnabled)
        {
            configurations.Add((
                "banfiles",
                JsonSerializer.Serialize(new
                {
                    checkIntervalSeconds = model.BanFileSyncConfigCheckIntervalSeconds
                }, configJsonOptions)));
        }

        if (model.GameServer.ServerListEnabled)
        {
            configurations.Add((
                "serverlist",
                JsonSerializer.Serialize(new
                {
                    htmlBanner = model.ServerListConfigHtmlBanner
                }, configJsonOptions)));
        }

        return configurations;
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }
}