namespace QuestAlarm.Application.Alarms;

public sealed record UpdateAlarmCommand(
    string? Title,
    string? Time,
    string? StartDate,
    IReadOnlyCollection<string>? RecurringDays);
