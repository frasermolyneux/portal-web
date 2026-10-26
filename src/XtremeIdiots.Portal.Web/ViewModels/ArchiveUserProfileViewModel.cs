using System.ComponentModel.DataAnnotations;

namespace XtremeIdiots.Portal.Web.ViewModels;

public class ArchiveUserProfileViewModel
{
    public Guid Id { get; set; }

    public string? DisplayName { get; set; }

    public string? ForumId { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    [Required]
    [StringLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = "Upstream forum account no longer exists.";
}
