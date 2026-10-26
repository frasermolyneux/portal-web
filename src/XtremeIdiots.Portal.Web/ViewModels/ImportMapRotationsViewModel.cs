using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class ImportMapRotationsViewModel
{
    [Required]
    [DisplayName("Game")]
    public GameType GameType { get; set; }

    [DisplayName("Config Content")]
    public string? CfgContent { get; set; }

    [DisplayName("Config File")]
    public IFormFile? CfgFile { get; set; }

    [DisplayName("Read config from a game server")]
    public bool FromGameServer { get; set; }

    [DisplayName("Game Server")]
    public Guid? GameServerId { get; set; }

    [DisplayName("Server Config File")]
    public string? ServerCfgPath { get; set; }

    [BindNever]
    public List<GameServerDto> AvailableServers { get; set; } = [];

    [BindNever]
    public string? ServerImportError { get; set; }
}

public class ImportMapRotationsPreviewViewModel
{
    public GameType GameType { get; set; }
    public List<ImportRotationPreviewItem> Rotations { get; set; } = [];
    public List<string> NewMapNames { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string DraftId { get; set; } = "";
}

public class ImportRotationPreviewItem
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameMode { get; set; } = "";
    public int MapCount { get; set; }
    public List<string> MapNames { get; set; } = [];
    public string ConfigVariableName { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Author { get; set; }
    public string? DateText { get; set; }
    public bool Selected { get; set; } = true;
    public bool IsDuplicate { get; set; }
    public string? DuplicateWarning { get; set; }
}

public class ImportMapRotationsConfirmViewModel
{
    public string DraftId { get; set; } = "";
    public string ImportPrefix { get; set; } = "";
    public List<int> SelectedIndices { get; set; } = [];
    public List<ImportRotationEditItem> Edits { get; set; } = [];
}

public class ImportRotationEditItem
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ImportMapRotationsResultViewModel
{
    public GameType GameType { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int MapsCreatedCount { get; set; }
    public List<ImportResultItem> Results { get; set; } = [];
}

public class ImportResultItem
{
    public string Title { get; set; } = "";
    public string Status { get; set; } = ""; // Imported, Skipped, Failed
    public string? Error { get; set; }
    public Guid? MapRotationId { get; set; }
}
