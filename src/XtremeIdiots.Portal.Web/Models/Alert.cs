namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Represents an alert message to be displayed to users
/// </summary>
/// <param name="Message">The message to display</param>
/// <param name="Type">The alert type (e.g., "alert-success", "alert-danger")</param>
public record Alert(string Message, string Type);