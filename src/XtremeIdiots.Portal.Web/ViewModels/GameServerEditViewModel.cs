using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;
using GameType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameType;
using RepoFileTransportType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model for the game server edit page with tabbed configuration
/// </summary>
public class GameServerEditViewModel : IValidatableObject
{
    public const int DefaultBroadcastIntervalSeconds = 500;
    public const int MaxFunnyMessageLength = 120;
    public const string DefaultScreenshotFilePattern = "*.jpg";
    public const int DefaultScreenshotPollIntervalSeconds = 60;
    public const int MinScreenshotPollIntervalSeconds = 10;
    public const int MaxScreenshotPollIntervalSeconds = 300;

    /// <summary>
    /// Core game server data
    /// </summary>
    public GameServerViewModel GameServer { get; set; } = new();

    // File transport configuration (parsed from "ftp" or "sftp" config namespace)

    [DisplayName("File Transport Hostname")]
    public string? FileTransportConfigHostname { get; set; }

    [DisplayName("File Transport Port")]
    public int FileTransportConfigPort { get; set; } = 21;

    [DisplayName("File Transport Username")]
    public string? FileTransportConfigUsername { get; set; }

    [DisplayName("File Transport Password")]
    public string? FileTransportConfigPassword { get; set; }

    [DisplayName("SFTP Host Key Fingerprint")]
    public string? FileTransportConfigHostKeyFingerprint { get; set; }

    [DisplayName("Maps Root Path")]
    public string? FileTransportConfigMapsRootPath { get; set; }

    // RCON configuration (parsed from "rcon" config namespace)

    [DisplayName("RCON Password")]
    public string? RconConfigPassword { get; set; }

    // Agent configuration (parsed from "agent" config namespace)

    [DisplayName("Log File Path")]
    public string? AgentConfigLogFilePath { get; set; }

    [DisplayName("RCON Sync Enabled")]
    public bool AgentConfigRconSyncEnabled { get; set; } = true;

    [DisplayName("Agent Name Override")]
    [MaxLength(120, ErrorMessage = "Agent name override must be 120 characters or fewer.")]
    public string? AgentConfigName { get; set; }

    // Screenshot configuration (parsed from "screenshots" config namespace)

    [DisplayName("Enable Screenshot Monitoring")]
    public bool ScreenshotConfigEnabled { get; set; }

    [DisplayName("Screenshot Directory Path")]
    public string? ScreenshotConfigDirectoryPath { get; set; }

    [DisplayName("Screenshot File Pattern")]
    public string? ScreenshotConfigFilePattern { get; set; } = DefaultScreenshotFilePattern;

    [DisplayName("Poll Interval (seconds)")]
    [Range(MinScreenshotPollIntervalSeconds, MaxScreenshotPollIntervalSeconds,
        ErrorMessage = "Screenshot poll interval must be between 10 and 300 seconds.")]
    public int ScreenshotConfigPollIntervalSeconds { get; set; } = DefaultScreenshotPollIntervalSeconds;

    // Ban File Sync configuration (parsed from "banfiles" config namespace)

    [DisplayName("Check Interval (seconds)")]
    [Range(10, 86400)]
    public int BanFileSyncConfigCheckIntervalSeconds { get; set; } = 60;

    // Server List configuration (parsed from "serverlist" config namespace)

    [DisplayName("HTML Banner")]
    [DataType(DataType.MultilineText)]
    public string? ServerListConfigHtmlBanner { get; set; }

    // Moderation configuration (parsed from "moderation" config namespace)

    [DisplayName("Protected Name Enforcement")]
    public bool ModerationProtectedNameEnforcementEnabled { get; set; } = true;

    [DisplayName("Hate Threshold")]
    [Range(-1, 6, ErrorMessage = "Hate threshold must be Use global default, Disabled, or between 0 and 6.")]
    public int? ModerationHateSeverityThreshold { get; set; }

    [DisplayName("Violence Threshold")]
    [Range(-1, 6, ErrorMessage = "Violence threshold must be Use global default, Disabled, or between 0 and 6.")]
    public int? ModerationViolenceSeverityThreshold { get; set; }

    [DisplayName("Sexual Threshold")]
    [Range(-1, 6, ErrorMessage = "Sexual threshold must be Use global default, Disabled, or between 0 and 6.")]
    public int? ModerationSexualSeverityThreshold { get; set; }

    [DisplayName("Self-Harm Threshold")]
    [Range(-1, 6, ErrorMessage = "Self-Harm threshold must be Use global default, Disabled, or between 0 and 6.")]
    public int? ModerationSelfHarmSeverityThreshold { get; set; }

    [DisplayName("Minimum Message Length")]
    [Range(1, int.MaxValue, ErrorMessage = "Minimum message length must be at least 1.")]
    public int? ModerationMinMessageLength { get; set; }

    // Events configuration (parsed from "events" config namespace)

    [DisplayName("Stale Event Threshold (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Stale event threshold must be at least 1 second.")]
    public int? EventsStaleThresholdSeconds { get; set; }

    [DisplayName("Player Cache Expiration (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Player cache expiration must be at least 1 second.")]
    public int? EventsPlayerCacheExpirationSeconds { get; set; }

    // Broadcasts configuration (parsed from "broadcasts" config namespace)

    [DisplayName("Enabled Override")]
    public TriStateOverrideValue BroadcastsEnabledOverride { get; set; } = TriStateOverrideValue.Inherit();

    [DisplayName("Enabled Override")]
    public bool? BroadcastsEnabled { get => BroadcastsEnabledOverride?.Value; set => BroadcastsEnabledOverride = TriStateOverrideValue.From(value); }

    [DisplayName("Interval (seconds)")]
    [Range(1, 86400, ErrorMessage = "Broadcast interval must be between 1 and 86400 seconds.")]
    public int? BroadcastsIntervalSeconds { get; set; } = DefaultBroadcastIntervalSeconds;

    public List<BroadcastMessageViewModel> BroadcastMessages { get; set; } = [];

    // Funny messages configuration (parsed from "funnyMessages" config namespace)

    public List<BroadcastMessageViewModel> FunnyMessages { get; set; } = [];

    // Global funny message defaults used for server-level inheritance awareness

    public List<BroadcastMessageViewModel> GlobalFunnyMessages { get; set; } = [];

    public bool GlobalBroadcastsEnabled { get; set; }

    public int GlobalBroadcastsIntervalSeconds { get; set; } = DefaultBroadcastIntervalSeconds;

    public List<BroadcastMessageViewModel> GlobalBroadcastMessages {
        get => GlobalFunnyMessages;
        set => GlobalFunnyMessages = value ?? [];
    }

    public string? GlobalServerListHtmlBanner { get; set; }

    // CoD4x plugin settings

    [DisplayName("Inherit Global CoD4x Plugin Settings")]
    public bool Cod4xInheritPluginSettings { get; set; } = true;

    [DisplayName("CoD4x Plugin Enabled")]
    public bool Cod4xPluginEnabled { get; set; }

    [DisplayName("Plugin Root Directory")]
    [MaxLength(512, ErrorMessage = "Plugin root directory must be 512 characters or fewer.")]
    public string? Cod4xPluginRootDirectory { get; set; }

    public string? Cod4xRuntimeCurrentVersion { get; set; }

    public string? Cod4xRuntimePreviousKnownGoodVersion { get; set; }

    public string? Cod4xRuntimeLastOperationId { get; set; }

    public Cod4xPluginOperationStatus Cod4xRuntimeLastOperationStatus { get; set; } = Cod4xPluginOperationStatus.Unknown;

    public DateTimeOffset? Cod4xRuntimeLastOperationUtc { get; set; }

    public string? Cod4xRuntimeLastError { get; set; }

    public string? Cod4xOperationRequestOperationId { get; set; }

    public Cod4xPluginOperationAction Cod4xOperationRequestAction { get; set; } = Cod4xPluginOperationAction.Unknown;

    public string? Cod4xOperationRequestTargetVersion { get; set; }

    public DateTimeOffset? Cod4xOperationRequestRequestedAtUtc { get; set; }

    public string? Cod4xOperationRequestRequestedBy { get; set; }

    // CoD4x power settings

    [DisplayName("Inherit Global CoD4x Power Settings")]
    public bool Cod4xInheritPowerSettings { get; set; } = true;

    [DisplayName("CoD4x Power Sync Enabled")]
    public bool Cod4xPowerEnabled { get; set; }

    [DisplayName("CoD4x Default Power")]
    [Range(Cod4xPowerSettingsConstants.MinPower, Cod4xPowerSettingsConstants.MaxPower,
        ErrorMessage = "Default power must be between 1 and 100.")]
    public int Cod4xPowerDefaultPower { get; set; } = Cod4xPowerSettingsConstants.DefaultPower;

    [DisplayName("CoD4x Power Tag Mappings (JSON)")]
    public string Cod4xPowerTagMappingsJson { get; set; } = "[]";

    public List<Cod4xPowerTagMappingViewModel> Cod4xPowerTagMappings { get; set; } = [];

    // CoD4x command settings

    [DisplayName("Inherit Global CoD4x Command Settings")]
    public bool Cod4xInheritCommandSettings { get; set; } = true;

    [DisplayName("CoD4x Command Power Enforcement Enabled")]
    public bool Cod4xCommandsEnabled { get; set; }

    public List<Cod4xCommandViewModel> Cod4xCommands { get; set; } = Cod4xSettingsViewModelHelpers.CreateDefaultCommands();

    // CoD4x global defaults for reference on server-level overrides

    public bool GlobalCod4xPluginEnabled { get; set; }

    public string? GlobalCod4xPluginRootDirectory { get; set; }

    public bool GlobalCod4xPowerEnabled { get; set; }

    public int GlobalCod4xPowerDefaultPower { get; set; } = Cod4xPowerSettingsConstants.DefaultPower;

    public bool GlobalCod4xCommandsEnabled { get; set; }

    public List<Cod4xCommandViewModel> GlobalCod4xCommands { get; set; } = Cod4xSettingsViewModelHelpers.CreateDefaultCommands();

    // Global defaults (for placeholder display in override fields)

    public int GlobalModerationHateSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationViolenceSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSexualSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSelfHarmSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationMinMessageLength { get; set; } = 5;
    public int GlobalEventsStaleThresholdSeconds { get; set; } = 120;
    public int GlobalEventsPlayerCacheExpirationSeconds { get; set; } = 900;
    public string GlobalAgentName { get; set; } = GlobalSettingsViewModel.DefaultAgentName;

    public ChatCommandServerSettingsViewModel ChatCommands { get; set; } = new();

    public ChatCommandGlobalSettingsViewModel GlobalChatCommands { get; set; } = new();

    public WelcomeMessageServerSettingsViewModel WelcomeMessages { get; set; } = new();

    public WelcomeMessageGlobalSettingsViewModel GlobalWelcomeMessages { get; set; } = new();

    public IReadOnlyList<RequiredTagOptionViewModel> AvailableRequiredTags { get; set; } = [];

    public bool IsRequiredTagsCatalogAvailable { get; private set; }

    // Auth flags for tab visibility

    public bool CanEditFileTransport { get; set; }
    public bool CanEditRcon { get; set; }
    public bool CanConfigureScreenshots { get; set; }

    public string FileTransportLabel => GetFileTransportLabel(GameServer.FileTransportType);
    public string FileTransportScheme => GetFileTransportScheme(GameServer.FileTransportType);
    public string FileTransportNamespace => GetFileTransportNamespace(GameServer.FileTransportType);
    public bool IsCod4xGameServer => GameServer.GameType == GameType.CallOfDuty4x;

    // Compatibility aliases retained for existing Razor/JS field names.
    public bool CanEditFtp { get => CanEditFileTransport; set => CanEditFileTransport = value; }
    public string? FtpConfigHostname { get => FileTransportConfigHostname; set => FileTransportConfigHostname = value; }
    public int FtpConfigPort { get => FileTransportConfigPort; set => FileTransportConfigPort = value; }
    public string? FtpConfigUsername { get => FileTransportConfigUsername; set => FileTransportConfigUsername = value; }
    public string? FtpConfigPassword { get => FileTransportConfigPassword; set => FileTransportConfigPassword = value; }
    public string? FtpConfigHostKeyFingerprint { get => FileTransportConfigHostKeyFingerprint; set => FileTransportConfigHostKeyFingerprint = value; }
    public string? FtpConfigMapsRootPath { get => FileTransportConfigMapsRootPath; set => FileTransportConfigMapsRootPath = value; }

    public static string GetFileTransportNamespace(RepoFileTransportType fileTransportType)
    {
        return fileTransportType == RepoFileTransportType.Sftp ? "sftp" : "ftp";
    }

    public static string GetFileTransportScheme(RepoFileTransportType fileTransportType)
    {
        return fileTransportType == RepoFileTransportType.Sftp ? "sftp" : "ftp";
    }

    public static string GetFileTransportLabel(RepoFileTransportType fileTransportType)
    {
        return fileTransportType == RepoFileTransportType.Sftp ? "SFTP" : "FTP";
    }

    public static int GetDefaultPort(RepoFileTransportType fileTransportType)
    {
        return fileTransportType == RepoFileTransportType.Sftp ? 22 : 21;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        SyncCod4xPowerTagMappings();

        if (BroadcastMessages is not null)
        {
            for (var i = 0; i < BroadcastMessages.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(BroadcastMessages[i].Message))
                    yield return new ValidationResult("Broadcast message is required.", [$"BroadcastMessages[{i}].Message"]);
            }
        }

        if (FunnyMessages is not null)
        {
            for (var i = 0; i < FunnyMessages.Count; i++)
            {
                if (FunnyMessages[i].Message?.Length > MaxFunnyMessageLength)
                    yield return new ValidationResult($"Funny message cannot exceed {MaxFunnyMessageLength} characters.", [$"FunnyMessages[{i}].Message"]);
            }
        }

        if (GameServer.AgentEnabled && ScreenshotConfigEnabled && string.IsNullOrWhiteSpace(ScreenshotConfigDirectoryPath))
        {
            yield return new ValidationResult("Screenshot directory path is required when screenshot monitoring is enabled.", [nameof(ScreenshotConfigDirectoryPath)]);
        }

        if (!string.IsNullOrWhiteSpace(ScreenshotConfigFilePattern) && ScreenshotConfigFilePattern.Trim().Length > 120)
        {
            yield return new ValidationResult("Screenshot file pattern must be 120 characters or fewer.", [nameof(ScreenshotConfigFilePattern)]);
        }

        foreach (var validationResult in ChatCommands.Validate(validationContext))
        {
            yield return validationResult;
        }

        foreach (var validationResult in WelcomeMessages.Validate(validationContext))
        {
            yield return validationResult;
        }

        if (IsCod4xGameServer && !Cod4xInheritPowerSettings)
        {
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Cod4xPowerTagMappings.Count; i++)
            {
                var mapping = Cod4xPowerTagMappings[i];
                if (string.IsNullOrWhiteSpace(mapping.Tag))
                {
                    yield return new ValidationResult($"CoD4x power mapping at index {i} is missing a tag.", [nameof(Cod4xPowerTagMappingsJson)]);
                    continue;
                }

                if (!seenTags.Add(mapping.Tag.Trim()))
                {
                    yield return new ValidationResult($"CoD4x power tag '{mapping.Tag}' is duplicated.", [nameof(Cod4xPowerTagMappingsJson)]);
                }

                if (mapping.Power is < 0 or > Cod4xPowerSettingsConstants.MaxPower)
                {
                    yield return new ValidationResult(
                        $"CoD4x power for tag '{mapping.Tag}' must be between 0 and {Cod4xPowerSettingsConstants.MaxPower}.",
                        [nameof(Cod4xPowerTagMappingsJson)]);
                }
            }
        }

        if (IsCod4xGameServer && !Cod4xInheritCommandSettings)
        {
            foreach (var command in Cod4xCommands)
            {
                if (command.MinPower is < 1 or > 100)
                {
                    yield return new ValidationResult("Each CoD4x command minimum power must be between 1 and 100.", [nameof(Cod4xCommands)]);
                    break;
                }
            }
        }
    }

    public void ApplyAvailableRequiredTags(
        IReadOnlyList<RequiredTagOptionViewModel> requiredTags,
        bool requiredTagsCatalogAvailable = true)
    {
        IsRequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        AvailableRequiredTags = requiredTags;
        SyncCod4xPowerTagMappings();
        ChatCommands.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        ChatCommands.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        WelcomeMessages.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        WelcomeMessages.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        GlobalChatCommands.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        GlobalChatCommands.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        GlobalWelcomeMessages.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        GlobalWelcomeMessages.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
    }

    public void SyncCod4xPowerTagMappings()
    {
        if (!IsRequiredTagsCatalogAvailable)
        {
            return;
        }

        Cod4xPowerTagMappings = Cod4xSettingsViewModelHelpers.BuildPowerTagMappings(
            AvailableRequiredTags,
            Cod4xPowerTagMappings,
            Cod4xPowerTagMappingsJson);

        Cod4xPowerTagMappingsJson = Cod4xSettingsViewModelHelpers.SerializePowerMappingsJson(
            Cod4xSettingsViewModelHelpers.BuildPowerTagMappingsForPersistence(Cod4xPowerTagMappings, Cod4xPowerTagMappingsJson));
    }
}

public class BroadcastMessageViewModel
{
    [DisplayName("Message")]
    public string Message { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;
}
