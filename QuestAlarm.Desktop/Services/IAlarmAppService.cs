using QuestAlarm.Core.Entities;

namespace QuestAlarm.Desktop.Services;

public interface IAlarmAppService
{
    Task<IReadOnlyList<Alarm>> GetAlarmsAsync(CancellationToken cancellationToken = default);
    Task<Alarm?> ToggleEnabledAsync(Guid alarmId, CancellationToken cancellationToken = default);
    Task<(Alarm? Alarm, string? Error)> CreateAsync(AlarmFormModel model, CancellationToken cancellationToken = default);
    Task<(Alarm? Alarm, string? Error)> UpdateAsync(Guid alarmId, AlarmFormModel model, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid alarmId, CancellationToken cancellationToken = default);
}

public sealed class AlarmFormModel
{
    public AlarmFormModel()
    {
    }

    public AlarmFormModel(
        string title,
        string time,
        bool isRecurring,
        string? startDate,
        string? recurringDaysCsv,
        string challengeType = "Typing",
        string challengeDifficulty = "Normal")
    {
        Title = title;
        Time = time;
        IsRecurring = isRecurring;
        StartDate = startDate;
        RecurringDaysCsv = recurringDaysCsv;
        ChallengeType = challengeType;
        ChallengeDifficulty = challengeDifficulty;
    }

    public string Title { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
    public string? StartDate { get; set; }
    public string? RecurringDaysCsv { get; set; }
    public string ChallengeType { get; set; } = "Typing";
    public string ChallengeDifficulty { get; set; } = "Normal";
}
