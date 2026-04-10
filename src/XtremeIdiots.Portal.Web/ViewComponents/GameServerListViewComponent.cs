using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ViewComponents;

/// <summary>
/// View component that displays a list of game servers with banners enabled
/// </summary>
/// <remarks>
/// Initializes a new instance of the GameServerListViewComponent
/// </remarks>
/// <param name="repositoryApiClient">Client for repository API operations</param>
public class GameServerListViewComponent(IRepositoryApiClient repositoryApiClient) : ViewComponent
{

    /// <summary>
    /// Retrieves and displays game servers that have banner functionality enabled
    /// </summary>
    /// <returns>View result with filtered game servers that have HTML banners</returns>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
            null, null, GameServerFilter.ServerListEnabled, 0, 50, GameServerOrder.ServerListPosition).ConfigureAwait(false);

        if (gameServersApiResponse.Result?.Data?.Items is null)
        {
            return View(Array.Empty<object>());
        }

        var allServers = gameServersApiResponse.Result.Data.Items.ToList();

        var serverConfigs = await GameServerConfigHelper.FetchConfigsForServersAsync(
            repositoryApiClient, allServers.Select(s => s.GameServerId)).ConfigureAwait(false);

        var filtered = allServers
            .Where(s => !string.IsNullOrWhiteSpace(
                GameServerConfigHelper.GetConfigValue(serverConfigs, s.GameServerId, "serverlist", "htmlBanner")))
            .ToList();

        ViewBag.ServerConfigs = serverConfigs;

        return View(filtered);
    }
}