using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Api.Abstractions;
using System.Globalization;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Redirect.Api.Abstractions.V1;

namespace XtremeIdiots.Portal.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[SkipStatusCodePages]
public sealed class MapDownloadsController(IRedirectApiClient redirect) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index()
    {
        return View();
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
        return request?.Paths is not { Length: >= 1 and <= 15 } ||
            request.Paths.Any(string.IsNullOrWhiteSpace) ||
            request.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Paths.Length
            ? Task.FromResult<IActionResult>(BadRequest(new { message = "Select between 1 and 15 distinct map or mod folders." }))
            : Read(async client =>
        {
            var result = await client.CreateArchive(request, cancellationToken).ConfigureAwait(false);
            return WithoutArchiveUrl(result);
        });
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
        if (!ModelState.IsValid)
            return BadRequest();
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null)
            return BadRequest(new { message = "The client address is unavailable." });
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var memberId = User.Identity?.IsAuthenticated == true ? User.XtremeIdiotsId() : null;
        if (User.Identity?.IsAuthenticated == true &&
            (!long.TryParse(memberId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0))
            return StatusCode(StatusCodes.Status403Forbidden);
        var userAgent = Request.Headers.UserAgent.ToString();
        var client = redirect.ForPortal(new PortalCaller(memberId, ip.ToString(), userAgent[..Math.Min(512, userAgent.Length)]));
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
                429 => "Too many requests. Please wait before trying again.",
                _ => "Maps Manager is unavailable. Please try again later."
            }
        });
    }

    private static ApiResult<ArchiveJob> WithoutArchiveUrl(ApiResult<ArchiveJob> result)
    {
        return result.IsSuccess && result.Result?.Data is { } job
            ? new ApiResult<ArchiveJob>(result.StatusCode, new ApiResponse<ArchiveJob>(job with { DownloadUrl = null }))
            : result;
    }
}
