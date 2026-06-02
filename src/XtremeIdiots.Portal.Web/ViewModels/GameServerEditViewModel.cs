using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using GameType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameType;
using RepoFileTransportType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model for the game server edit page with tabbed configuration
/// </summary>
public class GameServerEditViewModel : IValidatableObject
{
    public const int DefaultBroadcastIntervalSeconds = 500;
    public const int MaxBroadcastMessageLength = 120;
    public const int MaxFunnyMessageLength = 120;

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

    [DisplayName("Enabled")]
    public bool BroadcastsEnabled { get; set; }

    [DisplayName("Interval (seconds)")]
    [Range(1, 86400, ErrorMessage = "Broadcast interval must be between 1 and 86400 seconds.")]
    public int? BroadcastsIntervalSeconds { get; set; } = DefaultBroadcastIntervalSeconds;

    public List<BroadcastMessageViewModel> BroadcastMessages { get; set; } = [];

    // Funny messages configuration (parsed from "funnyMessages" config namespace)

    public List<BroadcastMessageViewModel> FunnyMessages { get; set; } = [];

    // Global funny message defaults used for server-level inheritance awareness

    public List<BroadcastMessageViewModel> GlobalFunnyMessages { get; set; } = [];

    // Global defaults (for placeholder display in override fields)

    public int GlobalModerationHateSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationViolenceSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSexualSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSelfHarmSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationMinMessageLength { get; set; } = 5;
    public int GlobalEventsStaleThresholdSeconds { get; set; } = 120;
    public int GlobalEventsPlayerCacheExpirationSeconds { get; set; } = 900;
    public string GlobalAgentName { get; set; } = GlobalSettingsViewModel.DefaultAgentName;

    // Auth flags for tab visibility

    public bool CanEditFileTransport { get; set; }
    public bool CanEditRcon { get; set; }

    public string FileTransportLabel => GetFileTransportLabel(GameServer.FileTransportType);
    public string FileTransportScheme => GetFileTransportScheme(GameServer.FileTransportType);
    public string FileTransportNamespace => GetFileTransportNamespace(GameServer.FileTransportType);

    // Legacy aliases kept for transition while Razor and JS migrate.
    public bool CanEditFtp { get => CanEditFileTransport; set => CanEditFileTransport = value; }
    public string? FtpConfigHostname { get => FileTransportConfigHostname; set => FileTransportConfigHostname = value; }
    public int FtpConfigPort { get => FileTransportConfigPort; set => FileTransportConfigPort = value; }
    public string? FtpConfigUsername { get => FileTransportConfigUsername; set => FileTransportConfigUsername = value; }
    public string? FtpConfigPassword { get => FileTransportConfigPassword; set => FileTransportConfigPassword = value; }
    public string? FtpConfigHostKeyFingerprint { get => FileTransportConfigHostKeyFingerprint; set => FileTransportConfigHostKeyFingerprint = value; }

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
        if (BroadcastMessages is not null)
        {
            for (var i = 0; i < BroadcastMessages.Count; i++)
            {
                if (BroadcastMessages[i].Message?.Length > MaxBroadcastMessageLength)
                    yield return new ValidationResult($"Broadcast message cannot exceed {MaxBroadcastMessageLength} characters.", [$"BroadcastMessages[{i}].Message"]);
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
    }
}

public class BroadcastMessageViewModel
{
    [DisplayName("Message")]
    public string Message { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;
}
