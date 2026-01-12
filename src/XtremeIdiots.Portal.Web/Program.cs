using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace XtremeIdiots.Portal.Web;

/// <summary>
/// Entry point for the XtremeIdiots Portal web application
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for the application
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    /// <summary>
    /// Creates the host builder with default configuration and Startup class
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configured host builder</returns>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                var builtConfig = configBuilder.Build();
                var appConfigEndpoint = builtConfig["AzureAppConfiguration:Endpoint"];

                if (string.IsNullOrWhiteSpace(appConfigEndpoint))
                {
                    return;
                }

                var managedIdentityClientId = builtConfig["AzureAppConfiguration:ManagedIdentityClientId"];
                var environmentLabel = builtConfig["AzureAppConfiguration:Environment"] ?? context.HostingEnvironment.EnvironmentName;

                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = managedIdentityClientId,
                });

                configBuilder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(new Uri(appConfigEndpoint), credential)
                        .Select(KeyFilter.Any, environmentLabel)
                        .Select(KeyFilter.Any, LabelFilter.Null)
                        .ConfigureKeyVault(kv => kv.SetCredential(credential));
                });
            })
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}