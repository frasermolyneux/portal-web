using System.Reflection;
using System.Security.Cryptography;
using System.Text;

var asm = Assembly.LoadFrom("/home/runner/work/portal-web/portal-web/src/XtremeIdiots.Portal.Web.IntegrationTests/bin/Debug/net10.0/XtremeIdiots.Portal.Web.IntegrationTests.dll");
var hostType = asm.GetType("XtremeIdiots.Portal.Web.IntegrationTests.Hosting.PortalWebTestHost");
var createAsync = hostType!.GetMethod("CreateAsync", BindingFlags.Public | BindingFlags.Static)!;
var task = (Task)createAsync.Invoke(null, new object?[] { null, CancellationToken.None })!;
await task;
var host = task.GetType().GetProperty("Result")!.GetValue(task)!;
var services = host.GetType().GetProperty("Services")!.GetValue(host)!;
var manifestType = asm.GetType("XtremeIdiots.Portal.Web.IntegrationTests.Manifest.PortalActionManifest");
var discover = manifestType!.GetMethod("Discover", BindingFlags.Public | BindingFlags.Static)!;
var discovered = discover.Invoke(null, new[] { services });
var entries = new List<string>();
foreach (var item in (System.Collections.IEnumerable)discovered!)
{
    var type = item.GetType();
    var key = type.GetProperty("Key")!.GetValue(item)!.ToString();
    var kind = type.GetProperty("Kind")!.GetValue(item)!.ToString();
    entries.Add($"{key}|{kind}");
}
entries.Sort(StringComparer.Ordinal);
var normalized = string.Join('\n', entries) + '\n';
var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
Console.WriteLine("HASH=" + hash);
var counts = entries
    .Select(x => x.Split('|').Last())
    .GroupBy(x => x, StringComparer.Ordinal)
    .OrderBy(g => g.Key)
    .Select(g => $"{g.Key}={g.Count()}")
    .ToArray();
Console.WriteLine("COUNTS=" + string.Join(", ", counts));
Console.WriteLine("TOTAL=" + entries.Count);
var dispose = host.GetType().GetMethod("DisposeAsync", BindingFlags.Public | BindingFlags.Instance)!;
await (ValueTask)dispose.Invoke(host, Array.Empty<object>())!;
