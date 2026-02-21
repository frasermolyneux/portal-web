using MX.InvisionCommunity.Api.Abstractions;
using XtremeIdiots.Portal.Integrations.Forums.Models;

namespace XtremeIdiots.Portal.Integrations.Forums;

/// <summary>
/// Manages demo client download information from the forums download system
/// </summary>
/// <param name="forumsClient">Invision Community API client for forum operations</param>
public class DemoManager(IInvisionApiClient forumsClient) : IDemoManager
{

    /// <summary>
    /// Retrieves the latest demo manager client download information from the forums
    /// </summary>
    /// <returns>Demo manager client information including version, description, URL, and changelog</returns>
    /// <exception cref="InvalidOperationException">Thrown when demo manager download file cannot be retrieved or URL is missing</exception>
    public async Task<DemoManagerClientDto> GetDemoManagerClient()
    {
        var downloadFileResult = await forumsClient.Downloads.GetDownloadFile(2753).ConfigureAwait(false);
        var downloadFile = downloadFileResult?.Result?.Data;

        return downloadFile is null
            ? throw new InvalidOperationException("Unable to retrieve demo manager download file from forums")
            : new DemoManagerClientDto
            {
                Version = downloadFile.Version ?? "Unknown",
                Description = downloadFile.Description ?? "No description available",
                Url = downloadFile.Url ?? throw new InvalidOperationException("Download URL is not available"),
                Changelog = downloadFile.Changelog ?? "No changelog available"
            };
    }
}