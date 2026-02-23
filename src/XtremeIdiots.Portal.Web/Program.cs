using Azure.Identity;

namespace XtremeIdiots.Portal.Web;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(builder =>
            {
                var builtConfig = builder.Build();
                var appConfigEndpoint = builtConfig["AzureAppConfiguration:Endpoint"];

                if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
                {
                    var managedIdentityClientId = builtConfig["AzureAppConfiguration:ManagedIdentityClientId"];
                    var environmentLabel = builtConfig["AzureAppConfiguration:Environment"];

                    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        ManagedIdentityClientId = managedIdentityClientId,
                    });

                    builder.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(new Uri(appConfigEndpoint), credential)
                            .Select("XtremeIdiots.Portal.Web:*", environmentLabel)
                            .TrimKeyPrefix("XtremeIdiots.Portal.Web:")
                            .Select("RepositoryApi:*", environmentLabel)
                            .Select("ServersIntegrationApi:*", environmentLabel)
                            .Select("GeoLocationApi:*", environmentLabel)
                            .Select("XtremeIdiots:*", environmentLabel)
                            .Select("ProxyCheck:*", environmentLabel)
                            .Select("GameTracker:*", environmentLabel)
                            .Select("Google:*", environmentLabel)
                            .Select("FeatureManagement:*", environmentLabel);

                        options.ConfigureKeyVault(kv => kv.SetCredential(credential));
                    });
                }
            })
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}