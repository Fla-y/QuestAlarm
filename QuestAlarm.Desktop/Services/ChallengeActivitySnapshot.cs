namespace QuestAlarm.Desktop.Services;

public sealed record ChallengeActivitySnapshot(
    Guid SessionId,
    DateTime LastActivityUtc,
    string Source);
