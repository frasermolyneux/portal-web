using System.Text.Json;
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
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed class NamespaceSettingsSerializer : INamespaceSettingsSerializer
{
    private readonly static JsonSerializerOptions configJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HashSet<string> DeletedNamespaces { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    IReadOnlyCollection<string> INamespaceSettingsSerializer.DeletedNamespaces => DeletedNamespaces;

    public IReadOnlyList<(string Namespace, string Configuration)> BuildGlobalSettingsConfigurations(GlobalSettingsViewModel model)
    {
        ResetDeletedNamespaces();

        List<(string Namespace, string Configuration)> configurations =
        [
            (
                AgentSettingsConstants.Namespace,
                JsonSerializer.Serialize(new AgentSettingsDocument
                {
                    PollIntervalMs = model.AgentPollIntervalMs,
                    StatusPublishIntervalSeconds = model.AgentStatusPublishIntervalSeconds,
                    RconSyncIntervalSeconds = model.AgentRconSyncIntervalSeconds,
                    OffsetSaveIntervalSeconds = model.AgentOffsetSaveIntervalSeconds,
                    AgentName = NormalizeAgentName(model.AgentName)
                }, configJsonOptions)
            ),
            (
                BanFileSettingsConstants.Namespace,
                JsonSerializer.Serialize(new BanFileSettingsDocument
                {
                    CheckIntervalSeconds = model.BanFileSyncCheckIntervalSeconds
                }, configJsonOptions)
            ),
            (
                ModerationSettingsConstants.Namespace,
                JsonSerializer.Serialize(new ModerationSettingsDocument
                {
                    ContentSafetyHateSeverityThreshold = model.ModerationHateSeverityThreshold,
                    ContentSafetyViolenceSeverityThreshold = model.ModerationViolenceSeverityThreshold,
                    ContentSafetySexualSeverityThreshold = model.ModerationSexualSeverityThreshold,
                    ContentSafetySelfHarmSeverityThreshold = model.ModerationSelfHarmSeverityThreshold,
                    MinMessageLength = model.ModerationMinMessageLength
                }, configJsonOptions)
            ),
            (
                EventSettingsConstants.Namespace,
                JsonSerializer.Serialize(new EventSettingsDocument
                {
                    StaleThresholdSeconds = model.EventsStaleThresholdSeconds,
                    PlayerCacheExpirationSeconds = model.EventsPlayerCacheExpirationSeconds
                }, configJsonOptions)
            )
        ];

        if (model.ChatCommands.DefaultsEnabled)
        {
            configurations.Add((
                ChatCommandSettingsConstants.Namespace,
                ChatCommandSettingsJsonMapper.BuildGlobalConfigurationJson(model.ChatCommands)));
        }
        else
        {
            DeletedNamespaces.Add(ChatCommandSettingsConstants.Namespace);
        }

        if (model.WelcomeMessages.Enabled)
        {
            configurations.Add((
                WelcomeMessageSettingsViewModelConstants.Namespace,
                WelcomeMessageSettingsJsonMapper.BuildGlobalConfigurationJson(model.WelcomeMessages)));
        }
        else
        {
            DeletedNamespaces.Add(WelcomeMessageSettingsViewModelConstants.Namespace);
        }

        configurations.Add((
            BroadcastSettingsConstants.Namespace,
            JsonSerializer.Serialize(new BroadcastSettingsDocument
            {
                Enabled = model.BroadcastsEnabled,
                IntervalSeconds = model.BroadcastsIntervalSeconds,
                Messages =
                [
                    .. (model.BroadcastMessages ?? []).Select(message => (BroadcastSettingsMessage?)new BroadcastSettingsMessage
                    {
                        Message = message.Message,
                        Enabled = message.Enabled
                    })
                ]
            }, configJsonOptions)));

        configurations.Add((
            ServerListSettingsConstants.Namespace,
            JsonSerializer.Serialize(new ServerListSettingsDocument
            {
                HtmlBanner = string.IsNullOrWhiteSpace(model.ServerListHtmlBanner)
                    ? null
                    : model.ServerListHtmlBanner
            }, configJsonOptions)));

        return configurations;
    }

    public IReadOnlyList<(string Namespace, string Configuration)> BuildGameServerConfigurations(
        GameServerEditViewModel model,
        bool canEditFileTransport,
        bool canEditRcon,
        bool canConfigureScreenshots)
    {
        ResetDeletedNamespaces();

        var configurations = new List<(string Namespace, string Configuration)>();
        var activeTransportNamespace = GameServerEditViewModel.GetFileTransportNamespace(model.GameServer.FileTransportType);

        if (canEditFileTransport)
        {
            if (string.Equals(activeTransportNamespace, "sftp", StringComparison.OrdinalIgnoreCase))
            {
                var sftpConfig = new SftpSettingsDocument
                {
                    Hostname = model.FileTransportConfigHostname,
                    Port = model.FileTransportConfigPort,
                    Username = model.FileTransportConfigUsername,
                    Password = model.FileTransportConfigPassword,
                    MapsRootPath = string.IsNullOrWhiteSpace(model.FileTransportConfigMapsRootPath)
                        ? null
                        : model.FileTransportConfigMapsRootPath,
                    HostKeyFingerprint = model.FileTransportConfigHostKeyFingerprint
                };

                configurations.Add((SftpSettingsConstants.Namespace, JsonSerializer.Serialize(sftpConfig, configJsonOptions)));
            }
            else
            {
                var ftpConfig = new FtpSettingsDocument
                {
                    Hostname = model.FileTransportConfigHostname,
                    Port = model.FileTransportConfigPort,
                    Username = model.FileTransportConfigUsername,
                    Password = model.FileTransportConfigPassword,
                    MapsRootPath = string.IsNullOrWhiteSpace(model.FileTransportConfigMapsRootPath)
                        ? null
                        : model.FileTransportConfigMapsRootPath
                };

                configurations.Add((FtpSettingsConstants.Namespace, JsonSerializer.Serialize(ftpConfig, configJsonOptions)));
            }
        }

        if (canEditRcon)
        {
            configurations.Add((
                RconSettingsConstants.Namespace,
                JsonSerializer.Serialize(new RconSettingsDocument
                {
                    Password = model.RconConfigPassword
                }, configJsonOptions)));
        }

        if (model.GameServer.AgentEnabled)
        {
            configurations.Add((
                AgentSettingsConstants.Namespace,
                JsonSerializer.Serialize(new AgentSettingsDocument
                {
                    LogFilePath = model.AgentConfigLogFilePath,
                    RconSyncEnabled = model.AgentConfigRconSyncEnabled,
                    AgentName = string.IsNullOrWhiteSpace(model.AgentConfigName)
                        ? null
                        : model.AgentConfigName
                }, configJsonOptions)));

            if (canConfigureScreenshots)
            {
                configurations.Add((
                    ScreenshotSettingsConstants.Namespace,
                    JsonSerializer.Serialize(new ScreenshotSettingsDocument
                    {
                        Enabled = model.ScreenshotConfigEnabled,
                        DirectoryPath = model.ScreenshotConfigEnabled ? model.ScreenshotConfigDirectoryPath : null,
                        FilePattern = string.IsNullOrWhiteSpace(model.ScreenshotConfigFilePattern)
                            ? GameServerEditViewModel.DefaultScreenshotFilePattern
                            : model.ScreenshotConfigFilePattern.Trim(),
                        PollIntervalSeconds = model.ScreenshotConfigPollIntervalSeconds
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
                var moderationConfig = new ModerationSettingsDocument
                {
                    ProtectedNameEnforcementEnabled = model.ModerationProtectedNameEnforcementEnabled
                };

                if (model.ModerationHateSeverityThreshold.HasValue)
                {
                    moderationConfig.ContentSafetyHateSeverityThreshold = model.ModerationHateSeverityThreshold.Value;
                }

                if (model.ModerationViolenceSeverityThreshold.HasValue)
                {
                    moderationConfig.ContentSafetyViolenceSeverityThreshold = model.ModerationViolenceSeverityThreshold.Value;
                }

                if (model.ModerationSexualSeverityThreshold.HasValue)
                {
                    moderationConfig.ContentSafetySexualSeverityThreshold = model.ModerationSexualSeverityThreshold.Value;
                }

                if (model.ModerationSelfHarmSeverityThreshold.HasValue)
                {
                    moderationConfig.ContentSafetySelfHarmSeverityThreshold = model.ModerationSelfHarmSeverityThreshold.Value;
                }

                if (model.ModerationMinMessageLength.HasValue)
                {
                    moderationConfig.MinMessageLength = model.ModerationMinMessageLength.Value;
                }

                configurations.Add((ModerationSettingsConstants.Namespace, JsonSerializer.Serialize(moderationConfig, configJsonOptions)));
            }
            else
            {
                configurations.Add((
                    ModerationSettingsConstants.Namespace,
                    JsonSerializer.Serialize(new ModerationSettingsDocument
                    {
                        ProtectedNameEnforcementEnabled = model.ModerationProtectedNameEnforcementEnabled
                    }, configJsonOptions)));
            }

            var hasEventsOverrides = model.EventsStaleThresholdSeconds.HasValue
                || model.EventsPlayerCacheExpirationSeconds.HasValue;

            if (hasEventsOverrides)
            {
                var eventsConfig = new EventSettingsDocument();

                if (model.EventsStaleThresholdSeconds.HasValue)
                {
                    eventsConfig.StaleThresholdSeconds = model.EventsStaleThresholdSeconds.Value;
                }

                if (model.EventsPlayerCacheExpirationSeconds.HasValue)
                {
                    eventsConfig.PlayerCacheExpirationSeconds = model.EventsPlayerCacheExpirationSeconds.Value;
                }

                configurations.Add((EventSettingsConstants.Namespace, JsonSerializer.Serialize(eventsConfig, configJsonOptions)));
            }
            else
            {
                configurations.Add((EventSettingsConstants.Namespace, JsonSerializer.Serialize(new EventSettingsDocument(), configJsonOptions)));
            }

            configurations.Add((
                ChatCommandSettingsConstants.Namespace,
                ChatCommandSettingsJsonMapper.BuildServerConfigurationJson(model.ChatCommands)));

            if (!HasChatCommandServerOverrides(model.ChatCommands))
            {
                configurations.RemoveAll(x => x.Namespace == ChatCommandSettingsConstants.Namespace);
                DeletedNamespaces.Add(ChatCommandSettingsConstants.Namespace);
            }

            configurations.Add((
                WelcomeMessageSettingsViewModelConstants.Namespace,
                WelcomeMessageSettingsJsonMapper.BuildServerConfigurationJson(model.WelcomeMessages)));

            if (!HasWelcomeMessageServerOverrides(model.WelcomeMessages))
            {
                configurations.RemoveAll(x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
                DeletedNamespaces.Add(WelcomeMessageSettingsViewModelConstants.Namespace);
            }

            var broadcastsIntervalSeconds = model.BroadcastsIntervalSeconds.GetValueOrDefault(GameServerEditViewModel.DefaultBroadcastIntervalSeconds);
            if (broadcastsIntervalSeconds <= 0)
            {
                broadcastsIntervalSeconds = GameServerEditViewModel.DefaultBroadcastIntervalSeconds;
            }

            var hasBroadcastOverrides = model.BroadcastsEnabled.HasValue;

            if (hasBroadcastOverrides)
            {
                configurations.Add((
                    BroadcastSettingsConstants.Namespace,
                    JsonSerializer.Serialize(new BroadcastSettingsDocument
                    {
                        Enabled = model.BroadcastsEnabled,
                        IntervalSeconds = broadcastsIntervalSeconds,
                        Messages =
                        [
                            .. (model.BroadcastMessages ?? []).Select(m => (BroadcastSettingsMessage?)new BroadcastSettingsMessage
                            {
                                Message = m.Message,
                                Enabled = m.Enabled
                            })
                        ]
                    }, configJsonOptions)));
            }
            else
            {
                DeletedNamespaces.Add(BroadcastSettingsConstants.Namespace);
            }
        }
        else
        {
            DeletedNamespaces.Add(AgentSettingsConstants.Namespace);
            DeletedNamespaces.Add(ScreenshotSettingsConstants.Namespace);
            DeletedNamespaces.Add(ModerationSettingsConstants.Namespace);
            DeletedNamespaces.Add(EventSettingsConstants.Namespace);
            DeletedNamespaces.Add(ChatCommandSettingsConstants.Namespace);
            DeletedNamespaces.Add(WelcomeMessageSettingsViewModelConstants.Namespace);
            DeletedNamespaces.Add(BroadcastSettingsConstants.Namespace);
        }

        if (model.GameServer.BanFileSyncEnabled)
        {
            configurations.Add((
                BanFileSettingsConstants.Namespace,
                JsonSerializer.Serialize(new BanFileSettingsDocument
                {
                    CheckIntervalSeconds = model.BanFileSyncConfigCheckIntervalSeconds
                }, configJsonOptions)));
        }
        else
        {
            DeletedNamespaces.Add(BanFileSettingsConstants.Namespace);
        }

        if (model.GameServer.ServerListEnabled)
        {
            configurations.Add((
                ServerListSettingsConstants.Namespace,
                JsonSerializer.Serialize(new ServerListSettingsDocument
                {
                    HtmlBanner = model.ServerListConfigHtmlBanner
                }, configJsonOptions)));
        }
        else
        {
            DeletedNamespaces.Add(ServerListSettingsConstants.Namespace);
        }

        return configurations;
    }

    private void ResetDeletedNamespaces()
    {
        DeletedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasChatCommandServerOverrides(ChatCommandServerSettingsViewModel model)
    {
        return model.Commands.Any(static command =>
            command.EnabledOverride.Value.HasValue
            || command.OverrideFreshness
            || command.OverrideRequiredTags
            || command.OverrideMessages);
    }

    private static bool HasWelcomeMessageServerOverrides(WelcomeMessageServerSettingsViewModel model)
    {
        return model.Enabled.HasValue
            || !model.InheritGlobalRules
            || !string.IsNullOrWhiteSpace(model.CountryFallback)
            || model.StaleThresholdSeconds.HasValue
            || model.DefaultConnectionDelaySeconds.HasValue
            || model.LocalRules.Count > 0
            || model.RuleOverrides.Count > 0;
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }
}