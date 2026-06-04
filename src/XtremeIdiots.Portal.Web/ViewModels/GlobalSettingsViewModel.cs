using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the global settings admin page, representing fleet-wide configuration defaults
/// </summary>
public class GlobalSettingsViewModel : IValidatableObject
{
    public const int DisabledSeverityThreshold = -1;
    public const string DefaultAgentName = "^4[^1>XI< BOT^4]^7";
    public const int MaxFunnyMessageLength = 120;

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

    [DisplayName("Agent Name")]
    [MaxLength(120, ErrorMessage = "Agent name must be 120 characters or fewer.")]
    public string AgentName { get; set; } = DefaultAgentName;

    // Ban file sync defaults
    [DisplayName("Check Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Check interval must be at least 1 second.")]
    public int BanFileSyncCheckIntervalSeconds { get; set; } = 60;

    // Moderation defaults
    [DisplayName("Hate Threshold")]
    [Range(-1, 6, ErrorMessage = "Hate threshold must be Disabled or between 0 and 6.")]
    public int ModerationHateSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Violence Threshold")]
    [Range(-1, 6, ErrorMessage = "Violence threshold must be Disabled or between 0 and 6.")]
    public int ModerationViolenceSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Sexual Threshold")]
    [Range(-1, 6, ErrorMessage = "Sexual threshold must be Disabled or between 0 and 6.")]
    public int ModerationSexualSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Self-Harm Threshold")]
    [Range(-1, 6, ErrorMessage = "Self-Harm threshold must be Disabled or between 0 and 6.")]
    public int ModerationSelfHarmSeverityThreshold { get; set; } = DisabledSeverityThreshold;

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

    // Global funny messages defaults

    public List<BroadcastMessageViewModel> FunnyMessages { get; set; } = [];

    public ChatCommandGlobalSettingsViewModel ChatCommands { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FunnyMessages is null)
            yield break;

        for (var i = 0; i < FunnyMessages.Count; i++)
        {
            if (FunnyMessages[i].Message?.Length > MaxFunnyMessageLength)
                yield return new ValidationResult($"Funny message cannot exceed {MaxFunnyMessageLength} characters.", [$"FunnyMessages[{i}].Message"]);
        }

        foreach (var validationResult in ChatCommands.Validate(validationContext))
        {
            yield return validationResult;
        }
    }

    public static IReadOnlyList<SelectListItem> BuildSeverityOptions(bool includeUseGlobalOption)
    {
        var options = new List<SelectListItem>();

        if (includeUseGlobalOption)
        {
            options.Add(new SelectListItem("Use global default", ""));
        }

        options.Add(new SelectListItem("Disabled", DisabledSeverityThreshold.ToString()));

        for (var i = 0; i <= 6; i++)
        {
            options.Add(new SelectListItem(i.ToString(), i.ToString()));
        }

        return options;
    }
}
