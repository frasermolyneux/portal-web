using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement("file-transport-path")]
[HtmlTargetElement("ftp-path")]
public class FtpPathTagHelper : TagHelper
{
    [HtmlAttributeName("host")] public string? Host { get; set; }
    [HtmlAttributeName("port")] public int Port { get; set; }
    [HtmlAttributeName("path")] public string? Path { get; set; }
    [HtmlAttributeName("user")] public string? User { get; set; }
    [HtmlAttributeName("password")] public string? Password { get; set; }
    [HtmlAttributeName("scheme")] public string? Scheme { get; set; }
    [HtmlAttributeName("show-credentials")] public bool ShowCredentials { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Path))
        {
            output.SuppressOutput();
            return;
        }

        var scheme = string.IsNullOrWhiteSpace(Scheme) ? "ftp" : Scheme.Trim().ToLowerInvariant();
        var basePath = $"{scheme}://{Host}:{Port}{Path}";
        if (!ShowCredentials || string.IsNullOrWhiteSpace(User) || string.IsNullOrWhiteSpace(Password))
        {
            output.TagName = "span";
            output.Content.SetContent(basePath);
            return;
        }

        output.TagName = "a";
        output.Attributes.SetAttribute("target", "_blank");
        output.Attributes.SetAttribute("href", $"{scheme}://{User}:{Password}@{Host}:{Port}{Path}");
        output.Content.SetContent(basePath);
    }
}
