using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class NamespaceSettingsParser : INamespaceSettingsParser
{
    public void PopulateGlobalSettingsViewModel(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
            {
                return;
            }

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
                    logger.LogDebug("Unknown global configuration namespace '{Namespace}'", config.Namespace);
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse global configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    public void PopulateGameServerSettingsViewModel(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
            {
                return;
            }

            using var doc = JsonDocument.Parse(config.Configuration);
            var root = doc.RootElement;

            switch (config.Namespace)
            {
                case "ftp":
                case "sftp":
                    var expectedNamespace = GameServerEditViewModel.GetFileTransportNamespace(model.GameServer.FileTransportType);
                    if (!string.Equals(config.Namespace, expectedNamespace, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    model.FileTransportConfigHostname = GetStringProperty(root, "hostname");
                    model.FileTransportConfigPort = GetIntProperty(root, "port", GameServerEditViewModel.GetDefaultPort(model.GameServer.FileTransportType));
                    model.FileTransportConfigUsername = GetStringProperty(root, "username");
                    model.FileTransportConfigPassword = GetStringProperty(root, "password");
                    model.FileTransportConfigHostKeyFingerprint = GetStringProperty(root, "hostKeyFingerprint");
                    model.FileTransportConfigMapsRootPath = GetStringProperty(root, "mapsRootPath");
                    break;
                case "rcon":
                    model.RconConfigPassword = GetStringProperty(root, "password");
                    break;
                case "agent":
                    model.AgentConfigLogFilePath = GetStringProperty(root, "logFilePath");
                    model.AgentConfigRconSyncEnabled = GetBoolProperty(root, "rconSyncEnabled", true);
                    model.AgentConfigName = GetStringProperty(root, "agentName");
                    break;
                case "screenshots":
                    model.ScreenshotConfigEnabled = GetBoolProperty(root, "enabled", false);
                    model.ScreenshotConfigDirectoryPath = GetStringProperty(root, "directoryPath");
                    model.ScreenshotConfigFilePattern = GetStringProperty(root, "filePattern") ?? GameServerEditViewModel.DefaultScreenshotFilePattern;
                    model.ScreenshotConfigPollIntervalSeconds = GetIntProperty(root, "pollIntervalSeconds", GameServerEditViewModel.DefaultScreenshotPollIntervalSeconds);
                    break;
                case "banfiles":
                    model.BanFileSyncConfigCheckIntervalSeconds = GetIntProperty(root, "checkIntervalSeconds", 60);
                    break;
                case "serverlist":
                    model.ServerListConfigHtmlBanner = GetStringProperty(root, "htmlBanner");
                    break;
                case "moderation":
                    model.ModerationProtectedNameEnforcementEnabled = GetBoolProperty(root, "protectedNameEnforcementEnabled", true);
                    var legacyThreshold = GetNullableIntProperty(root, "contentSafetySeverityThreshold");
                    model.ModerationHateSeverityThreshold = GetNullableIntProperty(root, "contentSafetyHateSeverityThreshold") ?? legacyThreshold;
                    model.ModerationViolenceSeverityThreshold = GetNullableIntProperty(root, "contentSafetyViolenceSeverityThreshold") ?? legacyThreshold;
                    model.ModerationSexualSeverityThreshold = GetNullableIntProperty(root, "contentSafetySexualSeverityThreshold") ?? legacyThreshold;
                    model.ModerationSelfHarmSeverityThreshold = GetNullableIntProperty(root, "contentSafetySelfHarmSeverityThreshold") ?? legacyThreshold;
                    model.ModerationMinMessageLength = GetNullableIntProperty(root, "minMessageLength");
                    break;
                case "events":
                    model.EventsStaleThresholdSeconds = GetNullableIntProperty(root, "staleThresholdSeconds");
                    model.EventsPlayerCacheExpirationSeconds = GetNullableIntProperty(root, "playerCacheExpirationSeconds");
                    break;
                case ChatCommandSettingsConstants.Namespace:
                    ChatCommandSettingsJsonMapper.PopulateServer(model.ChatCommands, root);
                    break;
                case WelcomeMessageSettingsViewModelConstants.Namespace:
                    WelcomeMessageSettingsJsonMapper.PopulateServer(model.WelcomeMessages, root);
                    break;
                case "broadcasts":
                    model.BroadcastsEnabled = GetBoolProperty(root, "enabled", false);
                    model.BroadcastsIntervalSeconds = GetNullableIntProperty(root, "intervalSeconds") ?? GameServerEditViewModel.DefaultBroadcastIntervalSeconds;
                    model.BroadcastMessages = GetBroadcastMessages(root);
                    break;
                default:
                    logger.LogDebug("Unknown configuration namespace '{Namespace}' for game server", config.Namespace);
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    public void PopulateGameServerGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
            {
                return;
            }

            using var doc = JsonDocument.Parse(config.Configuration);
            var root = doc.RootElement;

            switch (config.Namespace)
            {
                case "agent":
                    model.GlobalAgentName = NormalizeAgentName(GetStringProperty(root, "agentName"));
                    break;
                case "moderation":
                    var legacyThreshold = GetNullableIntProperty(root, "contentSafetySeverityThreshold");
                    model.GlobalModerationHateSeverityThreshold = GetIntProperty(root, "contentSafetyHateSeverityThreshold", legacyThreshold ?? model.GlobalModerationHateSeverityThreshold);
                    model.GlobalModerationViolenceSeverityThreshold = GetIntProperty(root, "contentSafetyViolenceSeverityThreshold", legacyThreshold ?? model.GlobalModerationViolenceSeverityThreshold);
                    model.GlobalModerationSexualSeverityThreshold = GetIntProperty(root, "contentSafetySexualSeverityThreshold", legacyThreshold ?? model.GlobalModerationSexualSeverityThreshold);
                    model.GlobalModerationSelfHarmSeverityThreshold = GetIntProperty(root, "contentSafetySelfHarmSeverityThreshold", legacyThreshold ?? model.GlobalModerationSelfHarmSeverityThreshold);
                    model.GlobalModerationMinMessageLength = GetIntProperty(root, "minMessageLength", model.GlobalModerationMinMessageLength);
                    break;
                case "events":
                    model.GlobalEventsStaleThresholdSeconds = GetIntProperty(root, "staleThresholdSeconds", model.GlobalEventsStaleThresholdSeconds);
                    model.GlobalEventsPlayerCacheExpirationSeconds = GetIntProperty(root, "playerCacheExpirationSeconds", model.GlobalEventsPlayerCacheExpirationSeconds);
                    break;
                case ChatCommandSettingsConstants.Namespace:
                    ChatCommandSettingsJsonMapper.PopulateGlobal(model.GlobalChatCommands, root);
                    break;
                case WelcomeMessageSettingsViewModelConstants.Namespace:
                    WelcomeMessageSettingsJsonMapper.PopulateGlobal(model.GlobalWelcomeMessages, root);
                    break;
                default:
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse global configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    public void PopulateGameServerDetails(IDictionary<string, object?> viewData, FileTransportType fileTransportType, ConfigurationDto config, ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
            {
                return;
            }

            using var doc = JsonDocument.Parse(config.Configuration);
            var root = doc.RootElement;

            switch (config.Namespace)
            {
                case "ftp":
                case "sftp":
                    var expectedNamespace = fileTransportType == FileTransportType.Sftp ? "sftp" : "ftp";
                    if (!string.Equals(config.Namespace, expectedNamespace, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    viewData["FtpHostname"] = GetStringProperty(root, "hostname");
                    viewData["FtpPort"] = GetIntProperty(root, "port", fileTransportType == FileTransportType.Sftp ? 22 : 21);
                    viewData["FtpUsername"] = GetStringProperty(root, "username");
                    viewData["FtpPassword"] = GetStringProperty(root, "password");
                    viewData["FileTransportType"] = fileTransportType;
                    break;
                case "rcon":
                    viewData["RconPassword"] = GetStringProperty(root, "password");
                    break;
                case "serverlist":
                    break;
                default:
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse details configuration for namespace '{Namespace}'", config.Namespace);
        }
    }

    public void PopulateExistingCredentials(
        GameServerEditViewModel model,
        string activeTransportNamespace,
        ConfigurationDto config,
        bool needsFileTransportPassword,
        bool needsFileTransportHostKeyFingerprint,
        bool needsRconPassword,
        ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Configuration))
            {
                return;
            }

            if ((needsFileTransportPassword || needsFileTransportHostKeyFingerprint)
                && string.Equals(config.Namespace, activeTransportNamespace, StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(config.Configuration);
                var root = doc.RootElement;

                if (needsFileTransportPassword)
                {
                    model.FileTransportConfigPassword = GetStringProperty(root, "password");
                }

                if (string.Equals(activeTransportNamespace, "sftp", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint))
                {
                    model.FileTransportConfigHostKeyFingerprint = GetStringProperty(root, "hostKeyFingerprint");
                }
            }
            else if (needsRconPassword && config.Namespace == "rcon")
            {
                using var doc = JsonDocument.Parse(config.Configuration);
                model.RconConfigPassword = GetStringProperty(doc.RootElement, "password");
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse credentials for namespace '{Namespace}'", config.Namespace);
        }
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static int? GetNullableIntProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool GetBoolProperty(JsonElement root, string propertyName, bool defaultValue)
    {
        return !root.TryGetProperty(propertyName, out var prop)
            ? defaultValue
            : prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(prop.GetString(), out var parsedBool) => parsedBool,
                JsonValueKind.Undefined => defaultValue,
                JsonValueKind.Object => defaultValue,
                JsonValueKind.Array => defaultValue,
                JsonValueKind.String => defaultValue,
                JsonValueKind.Number => defaultValue,
                JsonValueKind.Null => defaultValue,
                _ => defaultValue
            };
    }

    private static List<BroadcastMessageViewModel> GetBroadcastMessages(JsonElement root)
    {
        var messages = new List<BroadcastMessageViewModel>();

        if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (var item in messagesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            messages.Add(new BroadcastMessageViewModel
            {
                Message = GetStringProperty(item, "message") ?? string.Empty,
                Enabled = GetBoolProperty(item, "enabled", true)
            });
        }

        return messages;
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }
}