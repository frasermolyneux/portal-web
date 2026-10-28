using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Api.Abstractions;
using System.Globalization;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Redirect.Api.Abstractions.V1;

namespace XtremeIdiots.Portal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[SkipStatusCodePages]
public sealed class MapDownloadsController(IRedirectApiClient redirect) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index([FromServices] MapExperienceService maps, Guid? rotation = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest();
        if (!rotation.HasValue)
            return View();
        var client = PortalClient(out var error);
        if (client is null)
            return error!;
        try
        {
            var server = (await maps.Rotations(cancellationToken).ConfigureAwait(false)).Servers
                .SingleOrDefault(server => server.GameServerId == rotation);
            if (server is null)
                return NotFound();
            if (server.GameType is not (GameType.CallOfDuty4 or GameType.CallOfDuty4x or GameType.CallOfDuty5))
                return BadRequest("Map packs are available for COD4, COD4x and COD5.");
            var game = server.GameType == GameType.CallOfDuty5 ? "cod5" : "cod4";
            var available = await maps.DownloadPreview(server, ct => AvailableMaps(server, (page, version) =>
                client.Collection(game, "maps", page: page, pageSize: 200, version: version, cancellationToken: ct)), cancellationToken).ConfigureAwait(false);
            return !available.IsSuccess
                ? StatusCode((int)available.StatusCode, available.StatusCode == HttpStatusCode.RequestEntityTooLarge
                    ? "This pack contains more than 100 maps. Open Map and mod downloads (/MapDownloads) and choose smaller packs of up to 100 maps at a time."
                    : "We couldn't check the maps right now. Please try again later.")
                : View(available.Result!.Data);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, "We couldn't load this map pack. Please try again later.");
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IActionResult> Collection(string game, string kind = "maps", string search = "", int page = 1,
        string? version = null, CancellationToken cancellationToken = default)
    {
        return Read(client => client.Collection(game, kind, search, page, 50, version, cancellationToken));
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IActionResult> Browse(string path, string search = "", int page = 1, string? version = null,
        CancellationToken cancellationToken = default)
    {
        return Read(client => client.Browse(path, search, page, 50, version, cancellationToken));
    }

    [HttpGet]
    [AllowAnonymous]
    public Task<IActionResult> Manifest(string game, string mapName, string? version = null,
        CancellationToken cancellationToken = default)
    {
        return Read(client => client.Manifest(game, mapName, version, cancellationToken));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateArchive([FromBody] CreateArchive request, CancellationToken cancellationToken = default)
    {
        return request?.Paths is not { Length: >= 1 and <= 100 } ||
            request.Paths.Any(string.IsNullOrWhiteSpace) ||
            request.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Paths.Length
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "Select between 1 and 100 distinct map or mod folders." }))
            : Read(async client =>
        {
            var result = await client.CreateArchive(request, cancellationToken).ConfigureAwait(false);
            return WithoutArchiveUrl(result);
        });
    }

    public sealed record RotationArchiveRequest(Guid GameServerId);

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRotationArchive([FromBody] RotationArchiveRequest request,
        [FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || request is null || request.GameServerId == Guid.Empty)
            return BadRequest();
        PublicServerRotationDto? server;
        try
        {
            server = (await maps.Rotations(cancellationToken).ConfigureAwait(false)).Servers
                .SingleOrDefault(s => s.GameServerId == request.GameServerId);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new { message = "Map rotations are temporarily unavailable. Please try again later." });
        }

        if (server is null)
            return NotFound();
        if (server.GameType is not (GameType.CallOfDuty4 or GameType.CallOfDuty4x or GameType.CallOfDuty5))
            return BadRequest(new { message = "Map packs are available for COD4, COD4x and COD5." });
        var game = server.GameType == GameType.CallOfDuty5 ? "cod5" : "cod4";
        return await Read(async client =>
        {
            var available = await AvailableMaps(server, (page, version) =>
                client.Collection(game, "maps", page: page, pageSize: 200, version: version, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return !available.IsSuccess || available.Result?.Data is not { } selection
                ? new ApiResult<ArchiveJob>(available.StatusCode)
                : selection.Maps.Count == 0
                ? new ApiResult<ArchiveJob>(HttpStatusCode.UnprocessableEntity)
                : WithoutArchiveUrl(await client.CreateArchive(new CreateArchive(
                    [.. selection.Maps.Select(map => $"redirect/{game}/usermaps/{map.MapName}").Order(StringComparer.Ordinal)]),
                    cancellationToken).ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    private async static Task<ApiResult<PublicServerRotationDto>> AvailableMaps(PublicServerRotationDto server,
        Func<int, string?, Task<ApiResult<CataloguePage>>> readPage)
    {
        var names = server.Maps.Select(m => m.MapName).Where(n => !BuiltInMaps.IsBuiltIn(server.GameType, n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var game = server.GameType == GameType.CallOfDuty5 ? "cod5" : "cod4";
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? version = null;
        // Version-pinned collection pages avoid one manager request per map.
        for (var page = 1; page <= 50 && names.Count > 0; page++)
        {
            var response = await readPage(page, version).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data is not { } catalogue)
                return new ApiResult<PublicServerRotationDto>(response.IsSuccess ? HttpStatusCode.BadGateway : response.StatusCode);
            version = catalogue.Version;
            foreach (var entry in catalogue.Entries.Where(e => e.IsDirectory && e.Bytes > 0 && names.Contains(e.Name)
                && e.Path == $"redirect/{game}/usermaps/{e.Name}"))
            {
                found[entry.Name] = entry.Name;
                names.Remove(entry.Name);
            }

            if (found.Count > 100)
                return new ApiResult<PublicServerRotationDto>(HttpStatusCode.RequestEntityTooLarge);
            if (names.Count == 0 || (long)page * catalogue.PageSize >= catalogue.TotalCount)
                break;
            if (page == 50)
                return new ApiResult<PublicServerRotationDto>(HttpStatusCode.ServiceUnavailable);
        }

        return new ApiResult<PublicServerRotationDto>(HttpStatusCode.OK, new ApiResponse<PublicServerRotationDto>(server with
        {
            Maps = [.. server.Maps.Where(map => found.ContainsKey(map.MapName))
                .DistinctBy(map => map.MapName, StringComparer.OrdinalIgnoreCase)
                .Select(map => map with { MapName = found[map.MapName] })]
        }));
    }

    [HttpGet]
    [Authorize]
    public Task<IActionResult> ArchiveStatus(string id, CancellationToken cancellationToken = default)
    {
        return Read(async client => WithoutArchiveUrl(await client.ArchiveStatus(id, cancellationToken).ConfigureAwait(false)));
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Grant([FromBody] CreateDownloadGrant request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return Task.FromResult<IActionResult>(BadRequest());
        var archive = request.ArchiveId is not null;
        var invalidFile = string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Version);
        var invalidArchive = string.IsNullOrWhiteSpace(request.ArchiveId) || request.Path is not null || request.Version is not null;
        if (archive ? invalidArchive : invalidFile)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "An exact file version or one ZIP identifier is required." }));
        var requiresLogin = archive && User.Identity?.IsAuthenticated != true;
        return requiresLogin
            ? Task.FromResult<IActionResult>(Unauthorized())
            : Read(client => client.CreateDownloadGrant(request, cancellationToken));
    }

    private async Task<IActionResult> Read<T>(Func<IRedirectPortalClient, Task<ApiResult<T>>> operation) where T : class
    {
        var client = PortalClient(out var error);
        if (client is null)
            return error!;
        var result = await operation(client).ConfigureAwait(false);
        if (result.IsSuccess && result.Result?.Data is { } data)
            return Json(data);
        var status = (int)result.StatusCode;
        if (status is < 400 or > 599)
            status = StatusCodes.Status502BadGateway;
        return StatusCode(status, new
        {
            message = status switch
            {
                401 => "Sign in to Portal to continue.",
                403 => "This download is not available to your account.",
                404 => "The file or ZIP is no longer available.",
                409 or 412 => "The content has changed. Refresh the files and start a new download.",
                410 => "This ZIP has expired. Create a new ZIP.",
                413 => "This map pack is too large to download as one ZIP.",
                422 => "There are no extra maps to download for this server.",
                429 => "Too many requests. Please wait before trying again.",
                _ => "Maps Manager is unavailable. Please try again later."
            }
        });
    }

    private IRedirectPortalClient? PortalClient(out IActionResult? error)
    {
        error = null;
        if (!ModelState.IsValid)
        {
            error = BadRequest();
            return null;
        }

        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null)
        {
            error = BadRequest(new { message = "The client address is unavailable." });
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var memberId = User.Identity?.IsAuthenticated == true ? User.XtremeIdiotsId() : null;
        if (User.Identity?.IsAuthenticated == true &&
            (!long.TryParse(memberId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0))
        {
            error = StatusCode(StatusCodes.Status403Forbidden);
            return null;
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        return redirect.ForPortal(new PortalCaller(memberId, ip.ToString(), userAgent[..Math.Min(512, userAgent.Length)]));
    }

    private static ApiResult<ArchiveJob> WithoutArchiveUrl(ApiResult<ArchiveJob> result)
    {
        return result.IsSuccess && result.Result?.Data is { } job
            ? new ApiResult<ArchiveJob>(result.StatusCode, new ApiResponse<ArchiveJob>(job with { DownloadUrl = null }))
            : result;
    }
}
