using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize]
public sealed class RulesPreviewController(
    IRepositoryApiClient repositoryApiClient,
    IAuthorizationService authorizationService,
    IServersApiClient serversApiClient,
    IMemoryCache memoryCache,
    ILogger<RulesPreviewController> logger) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(131072)]
    public async Task<IActionResult> Preview([FromBody] RulesPreviewRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest("A preview request is required.");
        string? gameType = null;
        if (request.GameServerId is { } serverId)
        {
            var response = await repositoryApiClient.GameServers.V1.GetGameServer(serverId, cancellationToken).ConfigureAwait(false);
            if (response.IsNotFound)
                return NotFound();
            if (!response.IsSuccess || response.Result?.Data is not { } server || server.GameServerId != serverId)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!(await authorizationService.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Write).ConfigureAwait(false)).Succeeded)
                return Forbid();
            gameType = server.GameType.ToString();
        }
        else if (!(await authorizationService.AuthorizeAsync(User, AuthPolicies.GlobalSettings_Admin).ConfigureAwait(false)).Succeeded)
        {
            return Forbid();
        }

        var feature = RulesEngineCatalogueV1.Current.Features.FirstOrDefault(item => item.FeatureId == request.FeatureId);
        var action = RulesEngineCatalogueV1.Current.Actions.FirstOrDefault(item => item.ActionTypeId == request.ActionTypeId);
        if (feature is null || action is null || !feature.ActionTypeIds.Contains(action.ActionTypeId)
            || request.Mode is not ("sample" or "missing")
            || request.Parameters is null || request.TemplateParameters is null)
            return BadRequest("Invalid preview request.");

        var parameter = action.Parameters.FirstOrDefault(item => item.Name == request.ParameterName && item.SupportsTemplates);
        if (parameter is null || request.TemplateParameters.Any(name =>
                !action.Parameters.Any(item => item.Name == name && item.SupportsTemplates)))
            return BadRequest("The requested parameter does not support templates.");

        var values = request.Mode == "sample" ? RuleTextTokens.CreateSampleValues() : new RuleTextValues();
        var errors = new List<string>();
        var diagnostics = new List<string>();
        var warnings = new List<string>();
        var fragments = new List<PreparedMessageFragment>();
        var useCodColorCodes = true;

        string? Render(string name, string text)
        {
            if (!request.TemplateParameters.Contains(name))
                return text;
            var result = RuleTextRenderer.Render(text, values);
            errors.AddRange(result.Errors.Select(error => $"{error.Code} at position {error.Position}: {error.Message}"));
            diagnostics.AddRange(result.Diagnostics.Select(diagnostic => diagnostic.Message));
            return result.Text;
        }

        string? ReadString(string name)
        {
            if (!request.Parameters.TryGetValue(name, out var value))
                return null;
            if (value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{name} must be a string.");
                return null;
            }

            return value.GetString();
        }

        string? text = null;
        if (parameter.SupportsMessageList)
        {
            var message = ReadString("message");
            var renderedMessage = message is null ? null : Render("message", message);
            var renderedMessages = new List<string>();
            if (request.Parameters.TryGetValue("messages", out var messages))
            {
                if (messages.ValueKind != JsonValueKind.Array)
                    errors.Add("messages must be an array of strings.");
                else
                {
                    foreach (var item in messages.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            errors.Add("Each messages item must be a string.");
                        else if (Render("messages", item.GetString()!) is { } rendered)
                            renderedMessages.Add(rendered);
                    }
                }
            }

            if (errors.Count == 0)
            {
                var budget = new MessageChannelBudget(action.ActionTypeId, 149, Evidence: "Interim estimate; prefix is not known.");
                if (request.GameServerId is { } previewServerId)
                {
                    var channel = action.ActionTypeId == "message.public"
                        ? RuleMessageChannels.RconPublicChat
                        : RuleMessageChannels.RconPrivateChat;
                    var rcon = await GetRconBudgetAsync(previewServerId, channel, cancellationToken).ConfigureAwait(false);
                    if (rcon is { Status: RuleMessageBudgetStatus.Available, Budget: not null })
                    {
                        budget = rcon.Budget;
                        if (rcon.Constraints is { } constraints)
                            ValidateCharacterConstraints(renderedMessage, renderedMessages, constraints, errors);
                        warnings.Add($"RCON preview for {gameType}: {budget.MaxPayloadBytes} total bytes, including {budget.ReservedPrefixUtf8Bytes} reserved prefix bytes. {rcon.Detail ?? budget.Evidence}");
                        if (rcon.Constraints is null)
                            warnings.Add("The RCON character constraints were not reported; preview validation may be incomplete.");
                        warnings.Add("The eventual trigger may use another provider; prefix values may change and runtime-frozen fragments are authoritative.");
                    }
                    else
                    {
                        var status = rcon is null or { Status: RuleMessageBudgetStatus.Available }
                            ? "unavailable"
                            : rcon.Status.ToString().ToLowerInvariant();
                        warnings.Add($"RCON budget for {gameType} is {status}: {rcon?.Detail ?? "the budget lookup failed or returned incomplete data"}");
                        warnings.Add("Estimated fragments only: the 149-byte limit is an interim assumption and the server's configurable prefix is not included. Actual delivery may split differently.");
                    }
                }
                else
                {
                    warnings.Add("Estimated fragments only: no server or provider is selected; the 149-byte limit is an interim assumption and any server prefix is not included. Actual delivery may split differently.");
                }

                var preparation = MessagePreparation.Prepare(renderedMessage, renderedMessages,
                    budget);
                useCodColorCodes = budget.UseCodColorCodes;
                errors.AddRange(preparation.Errors.Select(error => error.Message));
                if (errors.Count == 0)
                    fragments.AddRange(preparation.Fragments);
            }
        }
        else if (ReadString(parameter.Name) is { } input)
        {
            text = Render(parameter.Name, input);
        }

        return Json(new { text, errors, diagnostics, warnings, fragments, useCodColorCodes });
    }

    private async Task<RuleMessageChannelBudgetDto?> GetRconBudgetAsync(Guid serverId, string channel, CancellationToken cancellationToken)
    {
        var key = (nameof(RulesPreviewController), serverId, channel);
        if (memoryCache.TryGetValue(key, out RuleMessageChannelBudgetDto? cached) && cached is not null)
            return cached;

        try
        {
            var response = await serversApiClient.RulesNativeAction.V1.GetMessageChannelBudget(serverId, channel, cancellationToken).ConfigureAwait(false);
            if (response is not { IsSuccess: true, Result.Data: not null })
            {
                logger.LogWarning("RCON message budget lookup failed for server {ServerId}", serverId);
                return null;
            }

            var budget = response.Result.Data;
            if (budget.Status == RuleMessageBudgetStatus.Available && budget.Budget is not null)
                memoryCache.Set(key, budget, TimeSpan.FromSeconds(10));
            return budget;
        }
        catch (Exception exception) when (exception is HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "RCON message budget lookup failed for server {ServerId}", serverId);
            return null;
        }
    }

    private static void ValidateCharacterConstraints(
        string? message, IEnumerable<string> messages, RconMessageCharacterConstraintsDto constraints, List<string> errors)
    {
        var items = message is null ? messages : new[] { message }.Concat(messages);
        var index = 0;
        foreach (var item in items)
        {
            if (item.Any(character =>
                    (constraints.AsciiOnly && character > 127)
                    || (constraints.ControlsDisallowed && char.IsControl(character))
                    || constraints.DisallowedCharacters.Contains(character))
                || (constraints.DisallowLocalizationInserts && HasLocalizationInsert(item)))
            {
                errors.Add($"Message {index + 1} contains characters unsupported by the RCON provider.");
            }

            index++;
        }
    }

    private static bool HasLocalizationInsert(string text)
    {
        for (var index = 0; index + 2 < text.Length; index++)
        {
            if (text[index] == '&' && text[index + 1] == '&' && text[index + 2] is >= '0' and <= '9')
                return true;
        }

        return false;
    }
}

public sealed class RulesPreviewRequest
{
    public string? FeatureId { get; init; }
    public Guid? GameServerId { get; init; }
    public string? ActionTypeId { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public List<string>? TemplateParameters { get; init; }
    public string? ParameterName { get; init; }
    public string? Mode { get; init; }
}
