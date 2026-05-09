namespace QuestAlarm.Desktop.Services;

public sealed record AlarmNotificationRequest(
    Guid AlarmId,
    string AlarmTitle,
    DateTime ScheduledForLocal,
    DateTime TriggeredAtLocal,
    Guid SessionId);
