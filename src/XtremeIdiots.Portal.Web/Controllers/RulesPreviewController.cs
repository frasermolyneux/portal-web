using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize]
public sealed class RulesPreviewController(
    IRepositoryApiClient repositoryApiClient,
    IAuthorizationService authorizationService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(131072)]
    public async Task<IActionResult> Preview([FromBody] RulesPreviewRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest("A preview request is required.");
        if (request.GameServerId is { } serverId)
        {
            var response = await repositoryApiClient.GameServers.V1.GetGameServer(serverId, cancellationToken).ConfigureAwait(false);
            if (response.IsNotFound)
                return NotFound();
            if (!response.IsSuccess || response.Result?.Data is not { } server || server.GameServerId != serverId)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!(await authorizationService.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Write).ConfigureAwait(false)).Succeeded)
                return Forbid();
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
                var preparation = MessagePreparation.Prepare(renderedMessage, renderedMessages,
                    new MessageChannelBudget(action.ActionTypeId, 149, Evidence: "Interim estimate; prefix is not known."));
                errors.AddRange(preparation.Errors.Select(error => error.Message));
                fragments.AddRange(preparation.Fragments);
            }
            warnings.Add("Estimated fragments only: the 149-byte limit is an interim assumption and the server's configurable prefix is not included. Actual delivery may split differently.");
        }
        else if (ReadString(parameter.Name) is { } input)
        {
            text = Render(parameter.Name, input);
        }

        return Json(new { text, errors, diagnostics, warnings, fragments });
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
