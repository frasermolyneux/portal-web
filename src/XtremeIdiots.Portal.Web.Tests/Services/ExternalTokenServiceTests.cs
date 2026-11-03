using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public class ExternalTokenServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-600)]
    public void ValidateToken_DoesNotLogUnverifiedForumMemberId(int ageSeconds)
    {
        var logger = new RecordingLogger();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["XtremeIdiots:ExternalWidget:HmacSecret"] = "synthetic-test-secret"
        }).Build();
        var service = new ExternalTokenService(configuration, logger);
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(ageSeconds).ToUnixTimeSeconds();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"forged\nlog-entry:{timestamp}:invalid"));

        var result = service.ValidateToken(token);

        Assert.False(result.IsValid);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("forged", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains('\n'));
    }

    private sealed class RecordingLogger : ILogger<ExternalTokenService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
