using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SiegeModeController(
    IRepositoryApiClient repository,
    IAuthorizationService authorization,
    INamespaceSettingsParser settingsParser,
    TelemetryClient telemetry,
    ILogger<SiegeModeController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetry, logger, configuration, auditLogger)
{
    [HttpGet]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var (server, error) = await GetAuthorizedServer(id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        var actor = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        var response = await repository.GlobalConfigurations.V1.GetConfiguration(SiegeModeSettingsConstants.Namespace, cancellationToken).ConfigureAwait(false);
        SiegeModeSettingsDocument? document;
        if (response.IsNotFound)
        {
            document = new SiegeModeSettingsDocument();
        }
        else if (!response.IsSuccess || response.Result?.Data is not { } config)
        {
            Logger.LogWarning("Cannot retrieve Siege Mode global settings: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        else if (!settingsParser.TryParseSiegeModeSettings(config, out document, out var errors) || document is null)
        {
            Logger.LogWarning("Invalid Siege Mode global settings: {Errors}", string.Join("; ", errors));
            return UnprocessableEntity("Siege Mode settings are invalid. Contact a senior administrator.");
        }

        return View(new SiegeModeActivationViewModel
        {
            GameServerId = id,
            ServerName = server!.Title,
            DurationMinutes = document.DurationMinutes,
            Actor = User.Username() ?? actor,
            OperationId = Guid.NewGuid()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id, Guid operationId, int expectedDurationMinutes, string? mode, CancellationToken cancellationToken)
    {
        var (_, error) = await GetAuthorizedServer(id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        var actor = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();
        if (operationId == Guid.Empty || expectedDurationMinutes <= 0 ||
            mode is not (nameof(SiegeSessionMode.DryRun) or nameof(SiegeSessionMode.Enforce)))
            return BadRequest();

        var response = await repository.SiegeMode.V1.StartSession(id, new StartSiegeSessionDto
        {
            OperationId = operationId,
            Actor = actor,
            ExpectedDurationMinutes = expectedDurationMinutes,
            Mode = Enum.Parse<SiegeSessionMode>(mode)
        }, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess && response.Result?.Data is not null)
        {
            this.AddAlertSuccess("Siege Mode activated.");
            return RedirectToAction("ServerDetail", "ServerAdmin", new { id });
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            this.AddAlertDanger("The session is already active or the duration changed. Review the current settings and confirm again.");
            return RedirectToAction(nameof(Activate), new { id });
        }

        Logger.LogWarning("Siege Mode activation failed for {ServerId}: {StatusCode}", id, response.StatusCode);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        var (_, error) = await GetAuthorizedServer(id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        var current = await repository.SiegeMode.V1.GetCurrentSession(id, cancellationToken).ConfigureAwait(false);
        if (current.IsNotFound)
            return PartialView("_Status", new SiegeModeStatusViewModel { ServerId = id });
        if (!current.IsSuccess || current.Result?.Data is not { } session)
        {
            Logger.LogWarning("Siege Mode status failed for {ServerId}: {StatusCode}", id, current.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var actions = await repository.SiegeMode.V1.GetActions(id, session.SessionId, 0, 50, cancellationToken).ConfigureAwait(false);
        var checks = await repository.SiegeMode.V1.GetChecks(id, session.SessionId, 0, 50, cancellationToken).ConfigureAwait(false);
        if (!actions.IsSuccess || actions.Result?.Data?.Items is null || !checks.IsSuccess || checks.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Siege Mode protection log failed for {ServerId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return PartialView("_Status", new SiegeModeStatusViewModel
        {
            ServerId = id,
            Session = session,
            Actions = actions.Result.Data.Items.ToArray(),
            Checks = checks.Result.Data.Items.ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Banner(CancellationToken cancellationToken)
    {
        var servers = new List<GameServerDto>();
        var seen = new HashSet<Guid>();
        const int pageSize = 100;
        for (var skip = 0; ; skip += pageSize)
        {
            var page = await repository.GameServers.V1.GetGameServers(null, null, null, skip, pageSize,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess || page.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Cannot retrieve servers for Siege Mode banner: {StatusCode}", page.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var items = page.Result.Data.Items.ToArray();
            if (items.Any(server => !seen.Add(server.GameServerId)))
            {
                Logger.LogWarning("Duplicate server in Siege Mode banner listing");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            servers.AddRange(items);
            if (items.Length < pageSize)
                break;
        }

        var active = new List<SiegeModeBannerItem>();
        foreach (var server in servers)
        {
            if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
                continue;
            var current = await repository.SiegeMode.V1.GetCurrentSession(server.GameServerId, cancellationToken).ConfigureAwait(false);
            if (current.IsNotFound)
                continue;
            if (!current.IsSuccess || current.Result?.Data is not { } session)
            {
                Logger.LogWarning("Cannot retrieve Siege Mode banner status for {ServerId}: {StatusCode}", server.GameServerId, current.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            active.Add(new SiegeModeBannerItem(server.GameServerId, server.Title, session));
        }

        return PartialView("_Banner", active);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> End(Guid id, Guid sessionId, Guid operationId, CancellationToken cancellationToken)
        => Change(id, sessionId, operationId, null, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Extend(Guid id, Guid sessionId, Guid operationId, int extensionMinutes, CancellationToken cancellationToken)
        => extensionMinutes <= 0 ? Task.FromResult<IActionResult>(BadRequest()) : Change(id, sessionId, operationId, extensionMinutes, cancellationToken);

    private async Task<IActionResult> Change(Guid id, Guid sessionId, Guid operationId, int? extensionMinutes, CancellationToken cancellationToken)
    {
        var (_, error) = await GetAuthorizedServer(id, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        var actor = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();
        if (sessionId == Guid.Empty || operationId == Guid.Empty)
            return BadRequest();
        var request = new ChangeSiegeSessionDto { OperationId = operationId, Actor = actor, ExtensionMinutes = extensionMinutes };
        var response = extensionMinutes is null
            ? await repository.SiegeMode.V1.EndSession(id, sessionId, request, cancellationToken).ConfigureAwait(false)
            : await repository.SiegeMode.V1.ExtendSession(id, sessionId, request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (response.IsNotFound || response.StatusCode == HttpStatusCode.Conflict)
            {
                this.AddAlertDanger("The Siege Mode session is no longer active. Refresh the server status.");
                return RedirectToAction("ServerDetail", "ServerAdmin", new { id });
            }

            Logger.LogWarning("Siege Mode update failed for {ServerId}: {StatusCode}", id, response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        this.AddAlertSuccess(extensionMinutes is null ? "Siege Mode ended." : "Siege Mode extended.");
        return RedirectToAction("ServerDetail", "ServerAdmin", new { id });
    }

    private async Task<(GameServerDto? Server, IActionResult? Error)> GetAuthorizedServer(Guid id, CancellationToken cancellationToken)
    {
        var response = await repository.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
        if (response.IsNotFound)
            return (null, NotFound());
        if (!response.IsSuccess || response.Result?.Data is not { } server || server.GameServerId != id)
        {
            Logger.LogWarning("Cannot retrieve Siege Mode server {ServerId}: {StatusCode}", id, response.StatusCode);
            return (null, StatusCode(StatusCodes.Status503ServiceUnavailable));
        }

        if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
            return (null, Forbid());
        return (server, null);
    }
}
