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

public sealed record AlarmFormModel(
    string Title,
    string Time,
    bool IsRecurring,
    string? StartDate,
    string? RecurringDaysCsv);
