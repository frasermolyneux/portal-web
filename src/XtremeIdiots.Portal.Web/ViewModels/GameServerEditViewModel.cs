using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model for the game server edit page with tabbed configuration
/// </summary>
public class GameServerEditViewModel : IValidatableObject
{
    /// <summary>
    /// Core game server data
    /// </summary>
    public GameServerViewModel GameServer { get; set; } = new();

    // FTP configuration (parsed from "ftp" config namespace)

    [DisplayName("FTP Hostname")]
    public string? FtpConfigHostname { get; set; }

    [DisplayName("FTP Port")]
    public int FtpConfigPort { get; set; } = 21;

    [DisplayName("FTP Username")]
    public string? FtpConfigUsername { get; set; }

    [DisplayName("FTP Password")]
    public string? FtpConfigPassword { get; set; }

    // RCON configuration (parsed from "rcon" config namespace)

    [DisplayName("RCON Password")]
    public string? RconConfigPassword { get; set; }

    // Agent configuration (parsed from "agent" config namespace)

    [DisplayName("Log File Path")]
    public string? AgentConfigLogFilePath { get; set; }

    [DisplayName("RCON Sync Enabled")]
    public bool AgentConfigRconSyncEnabled { get; set; } = true;

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
    public int? BroadcastsIntervalSeconds { get; set; } = 500;

    public List<BroadcastMessageViewModel> BroadcastMessages { get; set; } = [];

    // Global defaults (for placeholder display in override fields)

    public int GlobalModerationHateSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationViolenceSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSexualSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationSelfHarmSeverityThreshold { get; set; } = GlobalSettingsViewModel.DisabledSeverityThreshold;
    public int GlobalModerationMinMessageLength { get; set; } = 5;
    public int GlobalEventsStaleThresholdSeconds { get; set; } = 120;
    public int GlobalEventsPlayerCacheExpirationSeconds { get; set; } = 900;

    // Auth flags for tab visibility

    public bool CanEditFtp { get; set; }
    public bool CanEditRcon { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        for (var i = 0; i < BroadcastMessages.Count; i++)
        {
            if (BroadcastMessages[i].Message?.Length > 120)
                yield return new ValidationResult("Broadcast message cannot exceed 120 characters.", [$"BroadcastMessages[{i}].Message"]);
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
