namespace XtremeIdiots.Portal.Web.Helpers;

/// <summary>
/// Helper class for detecting and managing UITest mode configuration
/// </summary>
public static class UITestConfiguration
{
    /// <summary>
    /// Determines if the application is running in UITest mode
    /// </summary>
    /// <param name="configuration">The application configuration</param>
    /// <param name="environment">The hosting environment (optional)</param>
    /// <returns>True if UITest mode is enabled, false otherwise</returns>
    public static bool IsUITestMode(IConfiguration configuration, IWebHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Check environment name
        if (environment is not null && string.Equals(environment.EnvironmentName, "UITest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check configuration flag
        return string.Equals(configuration["UITest:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
    }
}
