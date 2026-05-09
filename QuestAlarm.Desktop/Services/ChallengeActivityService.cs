using System.Collections.Concurrent;
using Serilog;

namespace QuestAlarm.Desktop.Services;

public sealed class ChallengeActivityService : IChallengeActivityService
{
    private readonly ConcurrentDictionary<Guid, ChallengeActivitySnapshot> _activityBySession = new();
    private readonly ILogger _logger;

    public ChallengeActivityService(ILogger logger)
    {
        _logger = logger.ForContext<ChallengeActivityService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public ChallengeActivitySnapshot MarkActivity(Guid sessionId, string source)
    {
        var snapshot = new ChallengeActivitySnapshot(
            sessionId,
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim());

        _activityBySession[sessionId] = snapshot;

        _logger.Debug(
            "Challenge activity received {SessionId} {Source} {LastActivityUtc}",
            snapshot.SessionId,
            snapshot.Source,
            snapshot.LastActivityUtc);

        return snapshot;
    }

    public ChallengeActivitySnapshot? GetActivity(Guid sessionId)
    {
        return _activityBySession.GetValueOrDefault(sessionId);
    }

    public void ClearActivity(Guid sessionId)
    {
        _activityBySession.TryRemove(sessionId, out _);
    }
}
