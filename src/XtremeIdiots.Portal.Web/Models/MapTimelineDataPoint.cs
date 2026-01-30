namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Represents a data point for map timeline visualization showing when a specific map was active
/// </summary>
/// <param name="MapName">The name of the map</param>
/// <param name="Start">The start time when the map became active</param>
/// <param name="End">The end time when the map was no longer active</param>
public record MapTimelineDataPoint(string MapName, DateTime Start, DateTime End);