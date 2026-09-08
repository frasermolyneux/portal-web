namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

internal sealed record TestHostTimeouts
{
    internal TimeSpan Startup { get; init; } = TimeSpan.FromSeconds(60);
    internal TimeSpan Shutdown { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Startup.TotalMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Startup.TotalMilliseconds, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(Shutdown.TotalMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Shutdown.TotalMilliseconds, int.MaxValue);
    }
}
