using XtremeIdiots.Portal.Web.Helpers;

namespace XtremeIdiots.Portal.Web.Tests.Helpers;

public class AdminActionHtmlSanitizerTests
{
    [Theory]
    [InlineData("<script>alert(1)</script><p>Reason</p>", "script")]
    [InlineData("<img src=x onerror=alert(1)>", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">link</a>", "javascript:")]
    [InlineData("<svg onload=alert(1)></svg>", "onload")]
    [InlineData("<form action=\"https://example.com\"><input name=\"password\"></form>", "<form")]
    [InlineData("<span style=\"position:fixed;top:0\">overlay</span>", "position")]
    public void Sanitize_RemovesExecutableContent(string input, string forbidden)
    {
        var result = AdminActionHtmlSanitizer.Sanitize(input);

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_PreservesRichTextFormatting()
    {
        var result = AdminActionHtmlSanitizer.Sanitize("<p><strong>Reason</strong> <a href=\"https://example.com\">details</a></p>");

        Assert.Contains("<strong>Reason</strong>", result);
        Assert.Contains("href=\"https://example.com\"", result);
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmptyText()
    {
        Assert.Equal(string.Empty, AdminActionHtmlSanitizer.Sanitize(null));
    }
}
