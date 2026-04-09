using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the global settings admin page, representing fleet-wide configuration defaults
/// </summary>
public class GlobalSettingsViewModel
{
    // Agent defaults
    [DisplayName("Poll Interval (ms)")]
    [Range(100, int.MaxValue, ErrorMessage = "Poll interval must be at least 100ms.")]
    public int AgentPollIntervalMs { get; set; } = 500;

    [DisplayName("Status Publish Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Status publish interval must be at least 1 second.")]
    public int AgentStatusPublishIntervalSeconds { get; set; } = 60;

    [DisplayName("RCON Sync Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "RCON sync interval must be at least 1 second.")]
    public int AgentRconSyncIntervalSeconds { get; set; } = 300;

    [DisplayName("Offset Save Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Offset save interval must be at least 1 second.")]
    public int AgentOffsetSaveIntervalSeconds { get; set; } = 30;

    // Ban file sync defaults
    [DisplayName("Check Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Check interval must be at least 1 second.")]
    public int BanFileSyncCheckIntervalSeconds { get; set; } = 60;

    // Moderation defaults
    [DisplayName("Severity Threshold")]
    [Range(0, 6, ErrorMessage = "Severity threshold must be between 0 and 6.")]
    public int ModerationSeverityThreshold { get; set; } = 4;

    [DisplayName("Minimum Message Length")]
    [Range(1, int.MaxValue, ErrorMessage = "Minimum message length must be at least 1.")]
    public int ModerationMinMessageLength { get; set; } = 5;

    // Events defaults
    [DisplayName("Stale Event Threshold (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Stale event threshold must be at least 1 second.")]
    public int EventsStaleThresholdSeconds { get; set; } = 120;

    [DisplayName("Player Cache Expiration (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Player cache expiration must be at least 1 second.")]
    public int EventsPlayerCacheExpirationSeconds { get; set; } = 900;
}
