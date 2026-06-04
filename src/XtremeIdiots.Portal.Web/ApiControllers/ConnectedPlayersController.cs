using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize(Policy = AuthPolicies.AdminActions_Read)]
[Route("ConnectedPlayers")]
public class ConnectedPlayersController(
    IRepositoryApiClient repositoryApiClient,
    IMemoryCache memoryCache,
    TelemetryClient telemetryClient,
    ILogger<ConnectedPlayersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    private const int FetchBatchSize = 500;

    [HttpGet("SearchPlayers")]
    public async Task<IActionResult> SearchPlayers([FromQuery] string? term, [FromQuery] GameType? gameType, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                return Forbid();
            }

            var searchTerm = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            if (searchTerm is null || searchTerm.Length < 3 || gameType is null || gameType == GameType.Unknown)
            {
                return Ok(Array.Empty<object>());
            }

            var usernameMatches = new List<PlayerDto>();
            var skip = 0;
            const int pageSize = 50;
            const int maxScan = 1000;

            while (usernameMatches.Count < 20 && skip < maxScan)
            {
                var response = await repositoryApiClient.Players.V1
                    .GetPlayers(gameType, PlayersFilter.UsernameAndGuid, searchTerm, skip, pageSize, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None)
                    .ConfigureAwait(false);

                if (!response.IsSuccess || response.Result?.Data?.Items is null)
                {
                    Logger.LogWarning("Connected player search failed for term {Term}, game {GameType}, user {UserId}", searchTerm, gameType, User.XtremeIdiotsId());
                    return Ok(Array.Empty<object>());
                }

                var items = response.Result.Data.Items.ToList();
                if (items.Count == 0)
                {
                    break;
                }

                usernameMatches.AddRange(items.Where(x => x.Username.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
                skip += items.Count;
            }

            var data = usernameMatches
                .DistinctBy(x => x.PlayerId)
                .Take(20)
                .Select(x => new
                {
                    id = x.PlayerId,
                    text = x.Username,
                    username = x.Username,
                    guid = x.Guid,
                    ipAddress = x.IpAddress,
                    gameType = x.GameType.ToString(),
                    lastSeen = x.LastSeen
                })
                .ToArray();

            return Ok(data);
        }, nameof(SearchPlayers)).ConfigureAwait(false);
    }

    [HttpGet("SearchUserProfiles")]
    public async Task<IActionResult> SearchUserProfiles([FromQuery] string? term, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                return Forbid();
            }

            var searchTerm = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            if (searchTerm is null || searchTerm.Length < 2)
            {
                return Ok(Array.Empty<object>());
            }

            var response = await repositoryApiClient.UserProfiles.V1
                .GetUserProfiles(searchTerm, null, 0, 20, UserProfilesOrder.DisplayNameAsc, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Result?.Data?.Items is null)
            {
                Logger.LogWarning("User profile search failed for term {Term}, user {UserId}", searchTerm, User.XtremeIdiotsId());
                return Ok(Array.Empty<object>());
            }

            var data = response.Result.Data.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.XtremeIdiotsForumId))
                .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                .Select(x => new
                {
                    id = x.UserProfileId,
                    text = x.DisplayName,
                    displayName = x.DisplayName,
                    email = x.Email,
                    forumId = x.XtremeIdiotsForumId
                })
                .ToArray();

            return Ok(data);
        }, nameof(SearchUserProfiles)).ConfigureAwait(false);
    }

    [HttpGet("GetManualLinkPreview")]
    public async Task<IActionResult> GetManualLinkPreview([FromQuery] Guid? playerId, [FromQuery] Guid? userProfileId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                return Forbid();
            }

            if (playerId is null || playerId == Guid.Empty || userProfileId is null || userProfileId == Guid.Empty)
            {
                return BadRequest(new { message = "Both player and website profile must be selected." });
            }

            var playerResponse = await repositoryApiClient.Players.V1
                .GetPlayer(playerId.Value, PlayerEntityOptions.None)
                .ConfigureAwait(false);
            if (!playerResponse.IsSuccess || playerResponse.Result?.Data is null)
            {
                return NotFound(new { message = "Selected player was not found." });
            }

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfile(userProfileId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!userProfileResponse.IsSuccess || userProfileResponse.Result?.Data is null)
            {
                return NotFound(new { message = "Selected website profile was not found." });
            }

            var userProfile = userProfileResponse.Result.Data;
            if (string.IsNullOrWhiteSpace(userProfile.XtremeIdiotsForumId))
            {
                return BadRequest(new { message = "Selected website profile is missing a forum id and cannot be linked." });
            }

            var activeLinkResponse = await repositoryApiClient.ConnectedPlayers.V1
                .GetConnectedPlayers(playerId.Value, null, null, true, 0, 5, cancellationToken)
                .ConfigureAwait(false);

            var activeLinks = activeLinkResponse.IsSuccess
                ? activeLinkResponse.Result?.Data?.Items?.ToList() ?? []
                : [];

            var hasConflict = activeLinks.Any(x => x.UserProfileId != userProfileId.Value);
            var alreadyLinkedToSelectedProfile = activeLinks.Any(x => x.UserProfileId == userProfileId.Value);

            var player = playerResponse.Result.Data;

            return Ok(new
            {
                userProfile = ToPreviewUser(userProfile),
                player = ToPreviewPlayer(player),
                checks = new
                {
                    hasConflict,
                    alreadyLinkedToSelectedProfile,
                    canLink = !hasConflict && !alreadyLinkedToSelectedProfile,
                    message = hasConflict
                        ? "This player is already actively linked to a different website profile."
                        : alreadyLinkedToSelectedProfile
                            ? "This player is already linked to the selected website profile."
                            : "Ready to create manual link."
                }
            });
        }, nameof(GetManualLinkPreview)).ConfigureAwait(false);
    }

    [HttpPost("CreateManualLinkAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualLinkAjax([FromBody] CreateManualLinkAjaxRequest? request, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                return Forbid();
            }

            if (request is null || request.PlayerId == Guid.Empty || request.UserProfileId == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "Player and website profile are required." });
            }

            var linkedByUserProfileId = TryGetCurrentUserProfileId();
            if (linkedByUserProfileId is null)
            {
                return Forbid();
            }

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfile(request.UserProfileId, cancellationToken)
                .ConfigureAwait(false);
            if (!userProfileResponse.IsSuccess || userProfileResponse.Result?.Data is null)
            {
                return BadRequest(new { success = false, message = "Selected website profile was not found." });
            }

            if (string.IsNullOrWhiteSpace(userProfileResponse.Result.Data.XtremeIdiotsForumId))
            {
                return BadRequest(new { success = false, message = "Selected website profile is missing a forum id and cannot be linked." });
            }

            var activeLinkResponse = await repositoryApiClient.ConnectedPlayers.V1
                .GetConnectedPlayers(request.PlayerId, null, null, true, 0, 5, cancellationToken)
                .ConfigureAwait(false);

            if (!activeLinkResponse.IsSuccess)
            {
                return BadRequest(new { success = false, message = "Unable to validate existing links for the selected player." });
            }

            var activeLinks = activeLinkResponse.Result?.Data?.Items?.ToList() ?? [];
            if (activeLinks.Any(x => x.UserProfileId != request.UserProfileId))
            {
                return Conflict(new { success = false, message = "This player is already actively linked to a different website profile." });
            }

            if (activeLinks.Any(x => x.UserProfileId == request.UserProfileId))
            {
                return BadRequest(new { success = false, message = "This player is already linked to the selected website profile." });
            }

            var apiResult = await repositoryApiClient.ConnectedPlayers.V1
                .CreateConnectedPlayerLink(new CreateConnectedPlayerLinkDto
                {
                    PlayerId = request.PlayerId,
                    UserProfileId = request.UserProfileId,
                    LinkedByUserProfileId = linkedByUserProfileId,
                    LinkMethod = ConnectedPlayerLinkMethod.AdminForced
                }, cancellationToken)
                .ConfigureAwait(false);

            if (!apiResult.IsSuccess)
            {
                return BadRequest(new { success = false, message = "Failed to create connected player link. Confirm selections and try again." });
            }

            return Ok(new { success = true, message = "Connected player link created successfully." });
        }, nameof(CreateManualLinkAjax)).ConfigureAwait(false);
    }

    [HttpPost("GetConnectedPlayersAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetConnectedPlayersAjax([FromQuery] GameType? gameType = null, [FromQuery] bool? isActive = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);
            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model received for connected players from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            if (model.Length < 0 || model.Start < 0)
            {
                return BadRequest("Invalid pagination values");
            }

            if (model.Columns is null || model.Columns.Count == 0)
            {
                return BadRequest("Invalid columns metadata");
            }

            if (model.Order is not null && model.Order.Count > 0)
            {
                var requestedColumnIndex = model.Order.First().Column;
                if (requestedColumnIndex < 0 || requestedColumnIndex >= model.Columns.Count)
                {
                    return BadRequest("Invalid order column index");
                }
            }

            var cacheKey = $"connected-players:{gameType?.ToString() ?? "all"}:{isActive?.ToString() ?? "all"}";
            if (!memoryCache.TryGetValue(cacheKey, out List<ConnectedPlayerDto>? allRows) || allRows is null)
            {
                allRows = [];
                var skip = 0;
                int? totalCount = null;

                while (!totalCount.HasValue || skip < totalCount.Value)
                {
                    var response = await repositoryApiClient.ConnectedPlayers.V1
                        .GetConnectedPlayers(null, null, gameType, isActive, skip, FetchBatchSize, cancellationToken)
                        .ConfigureAwait(false);

                    if (!response.IsSuccess || response.Result?.Data?.Items is null)
                    {
                        Logger.LogWarning("Failed to retrieve connected players data for user {UserId}", User.XtremeIdiotsId());
                        return StatusCode(500, "Failed to retrieve connected players data");
                    }

                    var items = response.Result.Data.Items.ToList();
                    allRows.AddRange(items);

                    totalCount = response.Result.Pagination?.TotalCount ?? allRows.Count;

                    if (items.Count == 0)
                    {
                        break;
                    }

                    skip += items.Count;
                }

                memoryCache.Set(cacheKey, allRows, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                });
            }

            var recordsTotal = allRows.Count;

            IEnumerable<ConnectedPlayerDto> filtered = allRows;
            var searchTerm = model.Search?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(x =>
                    x.Username.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.PlayerId.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.UserProfileId.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.LinkMethod.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.GameType.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = ApplyOrdering(filtered, model).ToList();
            var recordsFiltered = filteredList.Count;

            var pageLength = model.Length <= 0 ? 10 : model.Length;
            var pageItems = filteredList
                .Skip(Math.Max(0, model.Start))
                .Take(pageLength)
                .Select(x => new
                {
                    connectedPlayerProfileId = x.ConnectedPlayerProfileId,
                    playerId = x.PlayerId,
                    userProfileId = x.UserProfileId,
                    gameType = x.GameType.ToString(),
                    username = x.Username,
                    linkMethod = x.LinkMethod.ToString(),
                    isActive = x.IsActive,
                    linkedAtUtc = x.LinkedAtUtc,
                    unlinkedAtUtc = x.UnlinkedAtUtc
                })
                .ToList();

            return Ok(new
            {
                model.Draw,
                recordsTotal,
                recordsFiltered,
                data = pageItems
            });
        }, nameof(GetConnectedPlayersAjax)).ConfigureAwait(false);
    }

    private static IEnumerable<ConnectedPlayerDto> ApplyOrdering(IEnumerable<ConnectedPlayerDto> source, DataTableAjaxPostModel model)
    {
        if (model.Order is null || model.Order.Count == 0)
        {
            return source.OrderByDescending(x => x.LinkedAtUtc);
        }

        var order = model.Order.First();
        var columnName = model.Columns[order.Column].Name;
        var isAsc = string.Equals(order.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        return columnName switch
        {
            "gameType" => isAsc ? source.OrderBy(x => x.GameType) : source.OrderByDescending(x => x.GameType),
            "username" => isAsc ? source.OrderBy(x => x.Username) : source.OrderByDescending(x => x.Username),
            "linkMethod" => isAsc ? source.OrderBy(x => x.LinkMethod) : source.OrderByDescending(x => x.LinkMethod),
            "isActive" => isAsc ? source.OrderBy(x => x.IsActive) : source.OrderByDescending(x => x.IsActive),
            "linkedAtUtc" => isAsc ? source.OrderBy(x => x.LinkedAtUtc) : source.OrderByDescending(x => x.LinkedAtUtc),
            "unlinkedAtUtc" => isAsc ? source.OrderBy(x => x.UnlinkedAtUtc) : source.OrderByDescending(x => x.UnlinkedAtUtc),
            _ => source.OrderByDescending(x => x.LinkedAtUtc)
        };
    }

    private static object ToPreviewPlayer(PlayerDto player)
    {
        return new
        {
            playerId = player.PlayerId,
            username = player.Username,
            guid = player.Guid,
            ipAddress = player.IpAddress,
            gameType = player.GameType.ToString(),
            lastSeen = player.LastSeen
        };
    }

    private static object ToPreviewUser(UserProfileDto userProfile)
    {
        return new
        {
            userProfileId = userProfile.UserProfileId,
            displayName = userProfile.DisplayName,
            email = userProfile.Email,
            forumId = userProfile.XtremeIdiotsForumId
        };
    }

    private bool IsSeniorAdminUser()
    {
        return User.HasClaim(claim => claim.Type == UserProfileClaimType.SeniorAdmin);
    }

    private Guid? TryGetCurrentUserProfileId()
    {
        return Guid.TryParse(User.UserProfileId(), out var profileId) ? profileId : null;
    }

    public sealed class CreateManualLinkAjaxRequest
    {
        public Guid PlayerId { get; init; }

        public Guid UserProfileId { get; init; }
    }
}
