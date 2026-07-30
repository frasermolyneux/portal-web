using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

internal enum PortalActionKind
{
    BrowserPage,
    HttpEndpoint,
    StateChange,
    DownloadOrStream,
    ExternalCallback,
}

internal sealed record PortalActionManifestEntry(string Key, PortalActionKind Kind)
{
    public string SnapshotLine => $"{Key}|{Kind}";
}

internal static class PortalActionManifest
{
    public const string ApprovedFingerprint = "dbbbbd37f8df42f06dcd3f5b499c37bbc424290bbf991e18456e9739b1209205";

    public static IReadOnlyDictionary<PortalActionKind, int> ApprovedCounts { get; } =
        new Dictionary<PortalActionKind, int>
        {
            [PortalActionKind.BrowserPage] = 87,
            [PortalActionKind.DownloadOrStream] = 3,
            [PortalActionKind.ExternalCallback] = 1,
            [PortalActionKind.HttpEndpoint] = 91,
            [PortalActionKind.StateChange] = 64,
        };

    private readonly static string[] downloadActionNames =
    [
        "ClientDownload",
        "Download",
        "MapImage",
    ];

    public static IReadOnlyList<PortalActionManifestEntry> Discover(IServiceProvider services)
    {
        var provider = services.GetRequiredService<IActionDescriptorCollectionProvider>();

        return provider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(descriptor => descriptor.ControllerTypeInfo.Assembly == typeof(PortalWebApplication).Assembly)
            .Select(descriptor => new PortalActionManifestEntry(CreateKey(descriptor), Classify(descriptor)))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static PortalActionKind Classify(ControllerActionDescriptor descriptor)
    {
        var httpMethods = GetHttpMethods(descriptor);
        var isSafeMethod = httpMethods.All(method => method is "ANY" or "GET" or "HEAD");
        var isApiController = descriptor.ControllerTypeInfo.Namespace?.Contains(".ApiControllers", StringComparison.Ordinal) == true;

        return descriptor.ActionName.Contains("Callback", StringComparison.Ordinal)
            ? PortalActionKind.ExternalCallback
            : downloadActionNames.Contains(descriptor.ActionName, StringComparer.Ordinal)
            ? PortalActionKind.DownloadOrStream
            : descriptor.ActionName.StartsWith("Get", StringComparison.Ordinal) ||
            descriptor.ActionName.StartsWith("Search", StringComparison.Ordinal) ||
            descriptor.ActionName is "Browse" or "Status" or "WhoAmI"
            ? PortalActionKind.HttpEndpoint
            : !isSafeMethod && (!isApiController || IsMutationAction(descriptor.ActionName))
            ? PortalActionKind.StateChange
            : isApiController ? PortalActionKind.HttpEndpoint : PortalActionKind.BrowserPage;
    }

    private static bool IsMutationAction(string actionName)
    {
        return actionName.StartsWith("Create", StringComparison.Ordinal) ||
            actionName.StartsWith("Delete", StringComparison.Ordinal) ||
            actionName.StartsWith("Force", StringComparison.Ordinal) ||
            actionName.StartsWith("Mark", StringComparison.Ordinal) ||
            actionName.StartsWith("Remove", StringComparison.Ordinal) ||
            actionName.StartsWith("Toggle", StringComparison.Ordinal) ||
            actionName.StartsWith("Update", StringComparison.Ordinal);
    }

    private static string CreateKey(ControllerActionDescriptor descriptor)
    {
        var controller = descriptor.ControllerTypeInfo.FullName
            ?? throw new InvalidOperationException("A controller action has no declaring type name.");
        var methods = string.Join(",", GetHttpMethods(descriptor));
        var route = descriptor.AttributeRouteInfo?.Template ?? $"{descriptor.ControllerName}/{descriptor.ActionName}";
        var parameters = string.Join(",", descriptor.MethodInfo
            .GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select(parameter => FormatTypeName(parameter.ParameterType)));

        return $"{controller}#{descriptor.MethodInfo.Name}#{methods}#{route}#({parameters})";
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsArray)
            return $"{FormatTypeName(type.GetElementType()!)}[]";

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var genericName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var arityMarkerIndex = genericName.IndexOf('`', StringComparison.Ordinal);
        if (arityMarkerIndex >= 0)
        {
            genericName = genericName[..arityMarkerIndex];
        }

        return $"{genericName}<{string.Join(",", type.GetGenericArguments().Select(FormatTypeName))}>";
    }

    private static string[] GetHttpMethods(ControllerActionDescriptor descriptor)
    {
        var methods = descriptor.ActionConstraints?
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .Select(method => method.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return methods is { Length: > 0 } ? methods : ["ANY"];
    }
}
