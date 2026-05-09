namespace QuestAlarm.Desktop.Services;

public interface IAlarmNotificationService
{
    Task ShowTriggeredAlarmAsync(
        AlarmNotificationRequest request,
        CancellationToken cancellationToken = default);

    Task MarkChallengeRunningAsync(Guid sessionId);
    Task MarkChallengeCompletedAsync(Guid sessionId);
    Task MarkChallengeFailedAsync(Guid sessionId);
}
