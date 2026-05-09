namespace QuestAlarm.Desktop.Services;

public interface IChallengeActivityService
{
    ChallengeActivitySnapshot MarkActivity(Guid sessionId, string source);
    ChallengeActivitySnapshot? GetActivity(Guid sessionId);
    void ClearActivity(Guid sessionId);
}
