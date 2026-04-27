using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for game server configuration and management
/// </summary>
public class GameServerViewModel
{
    /// <summary>
    /// Gets or sets the unique identifier for this game server
    /// </summary>
    public Guid GameServerId { get; set; }

    /// <summary>
    /// Gets or sets the display title of the game server
    /// </summary>
    [Required]
    [MaxLength(60)]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the type of game this server runs
    /// </summary>
    [Required]
    [DisplayName("Game")]
    public GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the hostname or IP address of the game server
    /// </summary>
    [Required]
    public string? Hostname { get; set; }

    /// <summary>
    /// Gets or sets the port used for server queries and status checks
    /// </summary>
    [Required]
    [DisplayName("Query Port")]
    public int QueryPort { get; set; }

    /// <summary>
    /// Gets or sets whether agent functionality is enabled for this server
    /// </summary>
    [DisplayName("Agent Enabled")]
    public bool AgentEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether FTP integration is enabled for this server
    /// </summary>
    [DisplayName("FTP")]
    public bool FtpEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether RCON integration is enabled for this server
    /// </summary>
    [DisplayName("RCON")]
    public bool RconEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether ban file synchronization is enabled for this server
    /// </summary>
    [DisplayName("Ban File Sync")]
    public bool BanFileSyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets the FTP root path beneath which the ban file lives. The agent
    /// resolves the full path from this root plus per-game-type rules and the
    /// currently-running mod (e.g. <c>{root}/mods/&lt;mod&gt;/ban.txt</c> for CoD4/5,
    /// <c>{root}/ban.txt</c> for CoD2). Defaults to <c>"/"</c>.
    /// </summary>
    [DisplayName("Ban File Root Path")]
    [MaxLength(255)]
    public string BanFileRootPath { get; set; } = "/";

    /// <summary>
    /// Gets or sets whether this server appears in server lists
    /// </summary>
    [DisplayName("Server List")]
    public bool ServerListEnabled { get; set; }

    /// <summary>
    /// Gets or sets the display order position in server lists
    /// </summary>
    [DisplayName("Position")]
    public int ServerListPosition { get; set; }
}