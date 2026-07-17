using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages fleet-wide global configuration defaults for agents, ban files, moderation, and events
/// </summary>
[Authorize(Policy = AuthPolicies.GlobalSettings_Admin)]
public class GlobalSettingsController(
    IRepositoryApiClient repositoryApiClient,
    IGlobalSettingsService globalSettingsService,
    TelemetryClient telemetryClient,
    ILogger<GlobalSettingsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var model = new GlobalSettingsViewModel();
            var (requiredTagOptions, isRequiredTagsCatalogAvailable) = await GetAvailableRequiredTagsAsync(cancellationToken).ConfigureAwait(false);
            model.ApplyAvailableRequiredTags(requiredTagOptions, isRequiredTagsCatalogAvailable);

            try
            {
                var configsResult = await repositoryApiClient.GlobalConfigurations.V1
                    .GetConfigurations(cancellationToken).ConfigureAwait(false);

                if (configsResult.IsSuccess && configsResult.Result?.Data?.Items != null)
                {
                    foreach (var config in configsResult.Result.Data.Items)
                    {
                        PopulateModelFromNamespace(model, config);
                    }
                }
                else
                {
                    Logger.LogWarning("Failed to retrieve global configurations, using defaults");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch global configurations, using defaults");
            }

            return View(model);
        }, nameof(Index)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(GlobalSettingsViewModel model, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (requiredTagOptions, isRequiredTagsCatalogAvailable) = await GetAvailableRequiredTagsAsync(cancellationToken).ConfigureAwait(false);
            model.ApplyAvailableRequiredTags(requiredTagOptions, isRequiredTagsCatalogAvailable);
            if (isRequiredTagsCatalogAvailable)
            {
                foreach (var validationResult in VpnProtectionSettingsViewModelValidation.ValidateExcludedTags(
                    model.VpnProtection.ExcludedPlayerTagsCsv,
                    model.VpnProtection.AllowedExcludedPlayerTags))
                {
                    ModelState.AddModelError(
                        $"{nameof(GlobalSettingsViewModel.VpnProtection)}.{nameof(VpnProtectionGlobalSettingsViewModel.ExcludedPlayerTagsCsv)}",
                        validationResult.ErrorMessage ?? "The excluded player tag is invalid.");
                }
            }

            var modelStateResult = CheckModelState(model);
            if (modelStateResult is not null)
                return modelStateResult;

            model.AgentName = NormalizeAgentName(model.AgentName);

            var errors = new List<string>();
            var namespacesToUpsert = globalSettingsService.BuildNamespaceConfigurations(model);
            var upsertedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (ns, json) in namespacesToUpsert)
            {
                upsertedNamespaces.Add(ns);
                await UpsertConfigSafeAsync(ns, json, errors, cancellationToken).ConfigureAwait(false);
            }

            foreach (var ns in globalSettingsService.DeletedNamespaces)
            {
                if (upsertedNamespaces.Contains(ns))
                {
                    continue;
                }

                await DeleteConfigSafeAsync(ns, errors, cancellationToken).ConfigureAwait(false);
            }

            if (errors.Count > 0)
            {
                this.AddAlertDanger($"Failed to save configuration for: {string.Join(", ", errors)}");
            }
            else
            {
                this.AddAlertSuccess("Global settings saved successfully.");
                TrackSuccessTelemetry("GlobalSettingsUpdated", nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }, nameof(Index)).ConfigureAwait(false);
    }

    private void PopulateModelFromNamespace(GlobalSettingsViewModel model, ConfigurationDto config)
    {
        globalSettingsService.PopulateModelFromNamespace(model, config, Logger);
    }

    private static string NormalizeAgentName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? GlobalSettingsViewModel.DefaultAgentName
            : value;
    }

    private async Task UpsertConfigSafeAsync(
        string ns,
        string configJson,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryApiClient.GlobalConfigurations.V1.UpsertConfiguration(
                ns, new UpsertConfigurationDto { Configuration = configJson }, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("Failed to upsert global configuration namespace '{Namespace}'", ns);
                errors.Add(ns);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error upserting global configuration namespace '{Namespace}'", ns);
            errors.Add(ns);
        }
    }

    private async Task DeleteConfigSafeAsync(
        string ns,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryApiClient.GlobalConfigurations.V1
                .DeleteConfiguration(ns, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess && !result.IsNotFound)
            {
                Logger.LogWarning("Failed to delete global configuration namespace '{Namespace}'", ns);
                errors.Add(ns);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error deleting global configuration namespace '{Namespace}'", ns);
            errors.Add(ns);
        }
    }

    private async Task<(IReadOnlyList<RequiredTagOptionViewModel> Tags, bool IsCatalogAvailable)> GetAvailableRequiredTagsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var tags = new List<RequiredTagOptionViewModel>();
        var skip = 0;

        while (true)
        {
            var tagsResponse = await repositoryApiClient.Tags.V1.GetTags(skip, pageSize, cancellationToken).ConfigureAwait(false);
            if (!tagsResponse.IsSuccess || tagsResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve tags for global settings required tags list");
                return ([], false);
            }

            var page = tagsResponse.Result.Data.Items.Where(static tag => tag is not null).ToList();
            if (page.Count == 0)
            {
                break;
            }

            tags.AddRange(page
                .Where(static tag => !string.IsNullOrWhiteSpace(tag.Name))
                .Select(static tag => new RequiredTagOptionViewModel
                {
                    Name = tag.Name,
                    DisplayName = tag.Name
                }));

            var pagination = tagsResponse.Result.Pagination;
            if (pagination is not null)
            {
                var totalAvailable = Math.Max(pagination.TotalCount, pagination.FilteredCount);
                var nextSkip = pagination.Skip + pagination.Top;
                if (nextSkip <= skip || nextSkip >= totalAvailable)
                {
                    break;
                }

                skip = nextSkip;
                continue;
            }

            if (page.Count < pageSize)
            {
                break;
            }

            skip += page.Count;
        }

        return ([.. tags
            .GroupBy(static tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static tag => tag.DisplayName, StringComparer.OrdinalIgnoreCase)], true);
    }
}
