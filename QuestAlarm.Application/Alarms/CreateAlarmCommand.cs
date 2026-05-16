namespace QuestAlarm.Application.Alarms;

public sealed record CreateAlarmCommand(
    string Title,
    string Time,
    string? StartDate,
    bool IsRecurring,
    IReadOnlyCollection<string>? RecurringDays,
    string? ChallengeType = null,
    string? ChallengeDifficulty = null);
