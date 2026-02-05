using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Forums.Models;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.UITest.Fakes;

/// <summary>
/// Fake admin action topics service for UITest mode
/// </summary>
public class FakeAdminActionTopics : IAdminActionTopics
{
    private readonly ILogger<FakeAdminActionTopics> _logger;
    private int _nextTopicId = 1000;

    public FakeAdminActionTopics(ILogger<FakeAdminActionTopics> logger)
    {
        _logger = logger;
    }

    public Task<int> CreateTopicForAdminAction(
        AdminActionType type,
        GameType gameType,
        Guid playerId,
        string username,
        DateTime created,
        string text,
        string? adminId,
        CancellationToken cancellationToken = default)
    {
        var topicId = Interlocked.Increment(ref _nextTopicId);
        _logger.LogInformation(
            "Fake forum topic created: ID={TopicId}, Type={Type}, Game={GameType}, Player={PlayerId}",
            topicId, type, gameType, playerId);
        return Task.FromResult(topicId);
    }

    public Task UpdateTopicForAdminAction(
        int topicId,
        AdminActionType type,
        GameType gameType,
        Guid playerId,
        string username,
        DateTime created,
        string text,
        string? adminId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fake forum topic updated: ID={TopicId}, Type={Type}, Game={GameType}, Player={PlayerId}",
            topicId, type, gameType, playerId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake demo manager service for UITest mode
/// </summary>
public class FakeDemoManager : IDemoManager
{
    public Task<DemoManagerClientDto> GetDemoManagerClient()
    {
        var result = new DemoManagerClientDto
        {
            Version = "1.0.0-uitest",
            Description = "UITest demo manager client",
            Url = new Uri("https://example.com/demo-manager-uitest.zip"),
            Changelog = "UITest version"
        };
        return Task.FromResult(result);
    }
}
