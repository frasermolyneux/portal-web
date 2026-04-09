using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model for the game server edit page with tabbed configuration
/// </summary>
public class GameServerEditViewModel
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
    [DataType(DataType.Password)]
    public string? FtpConfigPassword { get; set; }

    // RCON configuration (parsed from "rcon" config namespace)

    [DisplayName("RCON Password")]
    [DataType(DataType.Password)]
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

    [DisplayName("Position")]
    public int ServerListConfigPosition { get; set; }

    [DisplayName("HTML Banner")]
    [DataType(DataType.MultilineText)]
    public string? ServerListConfigHtmlBanner { get; set; }

    // Auth flags for tab visibility

    public bool CanEditFtp { get; set; }
    public bool CanEditRcon { get; set; }
}
