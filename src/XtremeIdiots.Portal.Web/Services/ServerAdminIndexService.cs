using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class ServerAdminIndexService(IRepositoryApiClient repository, IAuthorizationService authorization)
{
    public async Task<ServerAdminIndexResult?> Load(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        string[] requiredClaims = [UserProfileClaimType.Webmaster, UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_Admin_Read];
        var (gameTypes, gameServerIds) = user.ClaimedGamesAndItemsForViewing(requiredClaims);
        var response = await repository.GameServers.V1.GetGameServers(gameTypes, gameServerIds,
            GameServerFilter.AgentEnabled, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
            return null;

        var liveResponse = await repository.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
        var liveAvailable = liveResponse.IsSuccess && liveResponse.Result?.Data?.Items is not null;
        var live = liveAvailable ? liveResponse.Result!.Data!.Items!.ToDictionary(status => status.ServerId) : [];
        var rows = new List<ServerAdminGameServerViewModel>();
        foreach (var server in response.Result.Data.Items)
        {
            if (!(await authorization.AuthorizeAsync(user, server.GameType, AuthPolicies.GameServers_Admin_Read).ConfigureAwait(false)).Succeeded)
                continue;

            var chat = await authorization.AuthorizeAsync(user, server.GameType, AuthPolicies.ChatLog_ReadServer).ConfigureAwait(false);
            var maps = await authorization.AuthorizeAsync(user, server.GameType, AuthPolicies.GameServers_Maps_Read).ConfigureAwait(false);
            live.TryGetValue(server.GameServerId, out var status);
            rows.Add(new ServerAdminGameServerViewModel
            {
                GameServer = server,
                LiveStatus = status,
                GameServerQueryStatus = new ServerQueryStatusResponseDto(),
                GameServerRconStatus = new ServerRconStatusResponseDto(),
                CanViewChatLog = chat.Succeeded && ChatLogSupportedGames.Games.Contains(server.GameType),
                CanManageMaps = maps.Succeeded
            });
        }

        return new ServerAdminIndexResult(rows, liveAvailable);
    }
}

public sealed record ServerAdminIndexResult(List<ServerAdminGameServerViewModel> Servers, bool LiveAvailable);
