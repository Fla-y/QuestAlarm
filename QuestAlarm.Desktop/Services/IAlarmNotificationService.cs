namespace QuestAlarm.Desktop.Services;

public interface IAlarmNotificationService
{
    Task ShowTriggeredAlarmAsync(
        AlarmNotificationRequest request,
        CancellationToken cancellationToken = default);

    Task MarkChallengeRunningAsync(Guid sessionId);
    Task MarkChallengeActivityAsync(ChallengeActivitySnapshot activity);
    Task MarkChallengeCompletedAsync(Guid sessionId);
    Task MarkChallengeFailedAsync(Guid sessionId);
    Task MarkChallengeLaunchFailedAsync(Guid sessionId, string errorMessage);
}
