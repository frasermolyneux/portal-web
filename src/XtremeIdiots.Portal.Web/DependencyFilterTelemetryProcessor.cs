using System;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;

namespace XtremeIdiots.Portal.Web;

/// <summary>
/// Filters out successful, fast dependency calls for configured dependency types
/// to reduce telemetry volume. Failed calls and calls exceeding the duration
/// threshold are always retained.
/// </summary>
public sealed class DependencyFilterTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor next;
    private readonly IConfiguration configuration;

    public DependencyFilterTelemetryProcessor(ITelemetryProcessor next, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(configuration);

        this.next = next;
        this.configuration = configuration;
    }

    public void Process(ITelemetry item)
    {
        if (item is DependencyTelemetry dependency && ShouldFilter(dependency))
            return;

        next.Process(item);
    }

    private bool ShouldFilter(DependencyTelemetry dependency)
    {
        if (string.IsNullOrEmpty(dependency.Type))
            return false;

        var excludedTypes = configuration["ApplicationInsights:DependencyFilter:ExcludedTypes"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var excludedPrefixes = configuration["ApplicationInsights:DependencyFilter:ExcludedTypePrefixes"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var typeMatches =
            (excludedTypes?.Any(t => string.Equals(dependency.Type, t, StringComparison.OrdinalIgnoreCase)) == true) ||
            (excludedPrefixes?.Any(p => dependency.Type.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true);

        if (!typeMatches)
            return false;

        if (dependency.Success != true)
            return false;

        var thresholdMs = double.TryParse(
            configuration["ApplicationInsights:DependencyFilter:DurationThresholdMs"], out var t) ? t : 1000;
        if (dependency.Duration.TotalMilliseconds > thresholdMs)
            return false;

        return true;
    }
}
