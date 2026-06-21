using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Events;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.FileTransport;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Moderation;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Rcon;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Screenshots;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.ViewModels;
using RepositoryFileTransportType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class NamespaceSettingsParser : INamespaceSettingsParser
{
    private readonly static System.Text.Json.JsonSerializerOptions serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void PopulateGlobalSettingsViewModel(GlobalSettingsViewModel model, ConfigurationDto config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return;
        }

        switch (config.Namespace)
        {
            case AgentSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out AgentSettingsDocument? agentDocument) && agentDocument is not null)
                {
                    model.AgentPollIntervalMs = agentDocument.PollIntervalMs ?? model.AgentPollIntervalMs;
                    model.AgentStatusPublishIntervalSeconds = agentDocument.StatusPublishIntervalSeconds ?? model.AgentStatusPublishIntervalSeconds;
                    model.AgentRconSyncIntervalSeconds = agentDocument.RconSyncIntervalSeconds ?? model.AgentRconSyncIntervalSeconds;
                    model.AgentOffsetSaveIntervalSeconds = agentDocument.OffsetSaveIntervalSeconds ?? model.AgentOffsetSaveIntervalSeconds;
                    model.AgentName = NormalizeAgentName(agentDocument.AgentName);
                }

                break;
            case BanFileSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out BanFileSettingsDocument? banFileDocument) && banFileDocument?.CheckIntervalSeconds is int checkIntervalSeconds)
                {
                    model.BanFileSyncCheckIntervalSeconds = checkIntervalSeconds;
                }

                break;
            case ModerationSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ModerationSettingsDocument? moderationDocument) && moderationDocument is not null)
                {
                    var legacyThreshold = moderationDocument.ContentSafetySeverityThreshold;
                    model.ModerationHateSeverityThreshold = moderationDocument.ContentSafetyHateSeverityThreshold ?? legacyThreshold ?? model.ModerationHateSeverityThreshold;
                    model.ModerationViolenceSeverityThreshold = moderationDocument.ContentSafetyViolenceSeverityThreshold ?? legacyThreshold ?? model.ModerationViolenceSeverityThreshold;
                    model.ModerationSexualSeverityThreshold = moderationDocument.ContentSafetySexualSeverityThreshold ?? legacyThreshold ?? model.ModerationSexualSeverityThreshold;
                    model.ModerationSelfHarmSeverityThreshold = moderationDocument.ContentSafetySelfHarmSeverityThreshold ?? legacyThreshold ?? model.ModerationSelfHarmSeverityThreshold;
                    model.ModerationMinMessageLength = moderationDocument.MinMessageLength ?? model.ModerationMinMessageLength;
                }

                break;
            case EventSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out EventSettingsDocument? eventDocument) && eventDocument is not null)
                {
                    model.EventsStaleThresholdSeconds = eventDocument.StaleThresholdSeconds ?? model.EventsStaleThresholdSeconds;
                    model.EventsPlayerCacheExpirationSeconds = eventDocument.PlayerCacheExpirationSeconds ?? model.EventsPlayerCacheExpirationSeconds;
                }

                break;
            case ChatCommandSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ChatCommandSettingsDocument? chatCommandsDocument) && chatCommandsDocument is not null)
                {
                    ChatCommandSettingsJsonMapper.PopulateGlobal(model.ChatCommands, chatCommandsDocument);
                }

                break;
            case WelcomeMessageSettingsViewModelConstants.Namespace:
                if (TryDeserialize(config, logger, out WelcomeMessageSettingsDocument? welcomeMessagesDocument) && welcomeMessagesDocument is not null)
                {
                    WelcomeMessageSettingsJsonMapper.PopulateGlobal(model.WelcomeMessages, welcomeMessagesDocument);
                }

                break;
            case BroadcastSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out BroadcastSettingsDocument? broadcastDocument) && broadcastDocument is not null)
                {
                    model.BroadcastsEnabled = broadcastDocument.Enabled ?? false;
                    model.BroadcastsIntervalSeconds = broadcastDocument.IntervalSeconds ?? GameServerEditViewModel.DefaultBroadcastIntervalSeconds;
                    model.BroadcastMessages =
                    [
                        .. (broadcastDocument.Messages ?? [])
                            .Where(message => message is not null)
                            .Select(message => new BroadcastMessageViewModel
                            {
                                Message = message?.Message ?? string.Empty,
                                Enabled = message?.Enabled ?? true
                            })
                    ];
                }

                break;
            case ServerListSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ServerListSettingsDocument? serverListDocument) && serverListDocument is not null)
                {
                    model.ServerListHtmlBanner = serverListDocument.HtmlBanner;
                }

                break;
            default:
                logger.LogDebug("Unknown global configuration namespace '{Namespace}'", config.Namespace);
                break;
        }
    }

    public void PopulateGameServerSettingsViewModel(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return;
        }

        switch (config.Namespace)
        {
            case FtpSettingsConstants.Namespace:
            case SftpSettingsConstants.Namespace:
                var expectedNamespace = GameServerEditViewModel.GetFileTransportNamespace(model.GameServer.FileTransportType);
                if (!string.Equals(config.Namespace, expectedNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.Equals(config.Namespace, SftpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    && TryDeserialize(config, logger, out SftpSettingsDocument? sftpDocument)
                    && sftpDocument is not null)
                {
                    model.FileTransportConfigHostname = sftpDocument.Hostname;
                    model.FileTransportConfigPort = sftpDocument.Port ?? GameServerEditViewModel.GetDefaultPort(model.GameServer.FileTransportType);
                    model.FileTransportConfigUsername = sftpDocument.Username;
                    model.FileTransportConfigPassword = sftpDocument.Password;
                    model.FileTransportConfigHostKeyFingerprint = sftpDocument.HostKeyFingerprint;
                    model.FileTransportConfigMapsRootPath = sftpDocument.MapsRootPath;
                }
                else if (string.Equals(config.Namespace, FtpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                         && TryDeserialize(config, logger, out FtpSettingsDocument? ftpDocument)
                         && ftpDocument is not null)
                {
                    model.FileTransportConfigHostname = ftpDocument.Hostname;
                    model.FileTransportConfigPort = ftpDocument.Port ?? GameServerEditViewModel.GetDefaultPort(model.GameServer.FileTransportType);
                    model.FileTransportConfigUsername = ftpDocument.Username;
                    model.FileTransportConfigPassword = ftpDocument.Password;
                    model.FileTransportConfigMapsRootPath = ftpDocument.MapsRootPath;
                    model.FileTransportConfigHostKeyFingerprint = null;
                }

                break;
            case RconSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out RconSettingsDocument? rconDocument) && rconDocument is not null)
                {
                    model.RconConfigPassword = rconDocument.Password;
                }

                break;
            case AgentSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out AgentSettingsDocument? agentDocument) && agentDocument is not null)
                {
                    model.AgentConfigLogFilePath = agentDocument.LogFilePath;
                    model.AgentConfigRconSyncEnabled = agentDocument.RconSyncEnabled ?? true;
                    model.AgentConfigName = agentDocument.AgentName;
                }

                break;
            case ScreenshotSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ScreenshotSettingsDocument? screenshotDocument) && screenshotDocument is not null)
                {
                    model.ScreenshotConfigEnabled = screenshotDocument.Enabled ?? false;
                    model.ScreenshotConfigDirectoryPath = screenshotDocument.DirectoryPath;
                    model.ScreenshotConfigFilePattern = screenshotDocument.FilePattern ?? GameServerEditViewModel.DefaultScreenshotFilePattern;
                    model.ScreenshotConfigPollIntervalSeconds = screenshotDocument.PollIntervalSeconds ?? GameServerEditViewModel.DefaultScreenshotPollIntervalSeconds;
                }

                break;
            case BanFileSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out BanFileSettingsDocument? banFileDocument) && banFileDocument?.CheckIntervalSeconds is int checkIntervalSeconds)
                {
                    model.BanFileSyncConfigCheckIntervalSeconds = checkIntervalSeconds;
                }

                break;
            case ServerListSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ServerListSettingsDocument? serverListDocument) && serverListDocument is not null)
                {
                    model.ServerListConfigHtmlBanner = serverListDocument.HtmlBanner;
                }

                break;
            case ModerationSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ModerationSettingsDocument? moderationDocument) && moderationDocument is not null)
                {
                    var legacyThreshold = moderationDocument.ContentSafetySeverityThreshold;
                    model.ModerationProtectedNameEnforcementEnabled = moderationDocument.ProtectedNameEnforcementEnabled ?? true;
                    model.ModerationHateSeverityThreshold = moderationDocument.ContentSafetyHateSeverityThreshold ?? legacyThreshold;
                    model.ModerationViolenceSeverityThreshold = moderationDocument.ContentSafetyViolenceSeverityThreshold ?? legacyThreshold;
                    model.ModerationSexualSeverityThreshold = moderationDocument.ContentSafetySexualSeverityThreshold ?? legacyThreshold;
                    model.ModerationSelfHarmSeverityThreshold = moderationDocument.ContentSafetySelfHarmSeverityThreshold ?? legacyThreshold;
                    model.ModerationMinMessageLength = moderationDocument.MinMessageLength;
                }

                break;
            case EventSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out EventSettingsDocument? eventDocument) && eventDocument is not null)
                {
                    model.EventsStaleThresholdSeconds = eventDocument.StaleThresholdSeconds;
                    model.EventsPlayerCacheExpirationSeconds = eventDocument.PlayerCacheExpirationSeconds;
                }

                break;
            case ChatCommandSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ChatCommandSettingsDocument? chatCommandDocument) && chatCommandDocument is not null)
                {
                    ChatCommandSettingsJsonMapper.PopulateServer(model.ChatCommands, chatCommandDocument);
                }

                break;
            case WelcomeMessageSettingsViewModelConstants.Namespace:
                if (TryDeserialize(config, logger, out WelcomeMessageSettingsDocument? welcomeMessagesDocument) && welcomeMessagesDocument is not null)
                {
                    WelcomeMessageSettingsJsonMapper.PopulateServer(model.WelcomeMessages, welcomeMessagesDocument);
                }

                break;
            case BroadcastSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out BroadcastSettingsDocument? broadcastDocument) && broadcastDocument is not null)
                {
                    model.BroadcastsEnabled = broadcastDocument.Enabled ?? false;
                    model.BroadcastsIntervalSeconds = broadcastDocument.IntervalSeconds ?? GameServerEditViewModel.DefaultBroadcastIntervalSeconds;
                    model.BroadcastMessages =
                    [
                        .. (broadcastDocument.Messages ?? [])
                            .Where(message => message is not null)
                            .Select(message => new BroadcastMessageViewModel
                            {
                                Message = message?.Message ?? string.Empty,
                                Enabled = message?.Enabled ?? true
                            })
                    ];
                }

                break;
            default:
                logger.LogDebug("Unknown configuration namespace '{Namespace}' for game server", config.Namespace);
                break;
        }
    }

    public void PopulateGameServerGlobalDefaults(GameServerEditViewModel model, ConfigurationDto config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return;
        }

        switch (config.Namespace)
        {
            case AgentSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out AgentSettingsDocument? agentDocument) && agentDocument is not null)
                {
                    model.GlobalAgentName = NormalizeAgentName(agentDocument.AgentName);
                }

                break;
            case ModerationSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ModerationSettingsDocument? moderationDocument) && moderationDocument is not null)
                {
                    var legacyThreshold = moderationDocument.ContentSafetySeverityThreshold;
                    model.GlobalModerationHateSeverityThreshold = moderationDocument.ContentSafetyHateSeverityThreshold ?? legacyThreshold ?? model.GlobalModerationHateSeverityThreshold;
                    model.GlobalModerationViolenceSeverityThreshold = moderationDocument.ContentSafetyViolenceSeverityThreshold ?? legacyThreshold ?? model.GlobalModerationViolenceSeverityThreshold;
                    model.GlobalModerationSexualSeverityThreshold = moderationDocument.ContentSafetySexualSeverityThreshold ?? legacyThreshold ?? model.GlobalModerationSexualSeverityThreshold;
                    model.GlobalModerationSelfHarmSeverityThreshold = moderationDocument.ContentSafetySelfHarmSeverityThreshold ?? legacyThreshold ?? model.GlobalModerationSelfHarmSeverityThreshold;
                    model.GlobalModerationMinMessageLength = moderationDocument.MinMessageLength ?? model.GlobalModerationMinMessageLength;
                }

                break;
            case EventSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out EventSettingsDocument? eventDocument) && eventDocument is not null)
                {
                    model.GlobalEventsStaleThresholdSeconds = eventDocument.StaleThresholdSeconds ?? model.GlobalEventsStaleThresholdSeconds;
                    model.GlobalEventsPlayerCacheExpirationSeconds = eventDocument.PlayerCacheExpirationSeconds ?? model.GlobalEventsPlayerCacheExpirationSeconds;
                }

                break;
            case ChatCommandSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ChatCommandSettingsDocument? chatCommandsDocument) && chatCommandsDocument is not null)
                {
                    ChatCommandSettingsJsonMapper.PopulateGlobal(model.GlobalChatCommands, chatCommandsDocument);
                }

                break;
            case WelcomeMessageSettingsViewModelConstants.Namespace:
                if (TryDeserialize(config, logger, out WelcomeMessageSettingsDocument? welcomeMessagesDocument) && welcomeMessagesDocument is not null)
                {
                    WelcomeMessageSettingsJsonMapper.PopulateGlobal(model.GlobalWelcomeMessages, welcomeMessagesDocument);
                }

                break;
            case BroadcastSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out BroadcastSettingsDocument? broadcastDocument) && broadcastDocument is not null)
                {
                    model.GlobalBroadcastsEnabled = broadcastDocument.Enabled ?? false;
                    model.GlobalBroadcastsIntervalSeconds = broadcastDocument.IntervalSeconds ?? model.GlobalBroadcastsIntervalSeconds;
                    model.GlobalBroadcastMessages =
                    [
                        .. (broadcastDocument.Messages ?? [])
                            .Where(message => message is not null)
                            .Select(message => new BroadcastMessageViewModel
                            {
                                Message = message?.Message ?? string.Empty,
                                Enabled = message?.Enabled ?? true
                            })
                    ];
                }

                break;
            case ServerListSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out ServerListSettingsDocument? serverListDocument) && serverListDocument is not null)
                {
                    model.GlobalServerListHtmlBanner = serverListDocument.HtmlBanner;
                }

                break;
            default:
                break;
        }
    }

    public void PopulateGameServerDetails(IDictionary<string, object?> viewData, RepositoryFileTransportType fileTransportType, ConfigurationDto config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return;
        }

        switch (config.Namespace)
        {
            case FtpSettingsConstants.Namespace:
            case SftpSettingsConstants.Namespace:
                var expectedNamespace = fileTransportType == RepositoryFileTransportType.Sftp ? SftpSettingsConstants.Namespace : FtpSettingsConstants.Namespace;
                if (!string.Equals(config.Namespace, expectedNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.Equals(config.Namespace, SftpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                    && TryDeserialize(config, logger, out SftpSettingsDocument? sftpDocument)
                    && sftpDocument is not null)
                {
                    viewData["FtpHostname"] = sftpDocument.Hostname;
                    viewData["FtpPort"] = sftpDocument.Port ?? 22;
                    viewData["FtpUsername"] = sftpDocument.Username;
                    viewData["FtpPassword"] = sftpDocument.Password;
                    viewData["FileTransportType"] = fileTransportType;
                }
                else if (string.Equals(config.Namespace, FtpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                         && TryDeserialize(config, logger, out FtpSettingsDocument? ftpDocument)
                         && ftpDocument is not null)
                {
                    viewData["FtpHostname"] = ftpDocument.Hostname;
                    viewData["FtpPort"] = ftpDocument.Port ?? 21;
                    viewData["FtpUsername"] = ftpDocument.Username;
                    viewData["FtpPassword"] = ftpDocument.Password;
                    viewData["FileTransportType"] = fileTransportType;
                }

                break;
            case RconSettingsConstants.Namespace:
                if (TryDeserialize(config, logger, out RconSettingsDocument? rconDocument) && rconDocument is not null)
                {
                    viewData["RconPassword"] = rconDocument.Password;
                }

                break;
            case ServerListSettingsConstants.Namespace:
                break;
            default:
                break;
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
        if (string.IsNullOrWhiteSpace(config.Configuration))
        {
            return;
        }

        if ((needsFileTransportPassword || needsFileTransportHostKeyFingerprint)
            && string.Equals(config.Namespace, activeTransportNamespace, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(activeTransportNamespace, SftpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                && TryDeserialize(config, logger, out SftpSettingsDocument? sftpDocument)
                && sftpDocument is not null)
            {
                if (needsFileTransportPassword)
                {
                    model.FileTransportConfigPassword = sftpDocument.Password;
                }

                if (string.IsNullOrWhiteSpace(model.FileTransportConfigHostKeyFingerprint))
                {
                    model.FileTransportConfigHostKeyFingerprint = sftpDocument.HostKeyFingerprint;
                }
            }

            if (string.Equals(activeTransportNamespace, FtpSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase)
                && TryDeserialize(config, logger, out FtpSettingsDocument? ftpDocument)
                && ftpDocument is not null
                && needsFileTransportPassword)
            {
                model.FileTransportConfigPassword = ftpDocument.Password;
            }
        }
        else if (needsRconPassword && string.Equals(config.Namespace, RconSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase))
        {
            if (TryDeserialize(config, logger, out RconSettingsDocument? rconDocument) && rconDocument is not null)
            {
                model.RconConfigPassword = rconDocument.Password;
            }
        }
    }

    private static bool TryDeserialize<T>(ConfigurationDto config, ILogger logger, out T? document)
    {
        document = default;

        try
        {
            document = System.Text.Json.JsonSerializer.Deserialize<T>(config.Configuration, serializerOptions);
            return document is not null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            try
            {
                var normalizedJson = NormalizeBooleanStrings(config.Configuration);
                document = System.Text.Json.JsonSerializer.Deserialize<T>(normalizedJson, serializerOptions);
                return document is not null;
            }
            catch (System.Text.Json.JsonException)
            {
                logger.LogWarning(ex, "Failed to parse configuration for namespace '{Namespace}'", config.Namespace);
                return false;
            }
        }
    }

    private static string NormalizeBooleanStrings(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node is null)
        {
            return json;
        }

        NormalizeBooleanNodes(node);
        return node.ToJsonString();
    }

    private static void NormalizeBooleanNodes(System.Text.Json.Nodes.JsonNode node)
    {
        if (node is System.Text.Json.Nodes.JsonObject jsonObject)
        {
            var keys = jsonObject.Select(kvp => kvp.Key).ToArray();
            foreach (var key in keys)
            {
                var child = jsonObject[key];
                if (child is null)
                {
                    continue;
                }

                if (child is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var stringValue) && bool.TryParse(stringValue, out var parsedBool))
                {
                    jsonObject[key] = parsedBool;
                    continue;
                }

                NormalizeBooleanNodes(child);
            }

            return;
        }

        if (node is System.Text.Json.Nodes.JsonArray jsonArray)
        {
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var child = jsonArray[i];
                if (child is null)
                {
                    continue;
                }

                if (child is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var stringValue) && bool.TryParse(stringValue, out var parsedBool))
                {
                    jsonArray[i] = parsedBool;
                    continue;
                }

                NormalizeBooleanNodes(child);
            }
        }
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }
}