namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

internal sealed record BrowserRuntimeOptions
{
    internal int MaximumContexts { get; init; } = 2;
    internal TimeSpan PermitTimeout { get; init; } = TimeSpan.FromSeconds(120);
    internal TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(60);
    internal TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal TimeSpan NavigationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumContexts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumContexts, 2);
        ValidateTimeout(PermitTimeout);
        ValidateTimeout(LaunchTimeout);
        ValidateTimeout(ActionTimeout);
        ValidateTimeout(NavigationTimeout);
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout.TotalMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout.TotalMilliseconds, int.MaxValue);
    }
}
