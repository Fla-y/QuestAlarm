using QuestAlarm.Core.Entities;

namespace QuestAlarm.Application.Alarms;

public interface IAlarmManagementService
{
    Task<IReadOnlyList<Alarm>> GetAlarmsAsync(CancellationToken cancellationToken = default);
    Task<Alarm?> GetByIdAsync(Guid alarmId, CancellationToken cancellationToken = default);
    Task<AlarmMutationResult> CreateAsync(CreateAlarmCommand command, CancellationToken cancellationToken = default);
    Task<AlarmMutationResult> UpdateAsync(Guid alarmId, UpdateAlarmCommand command, CancellationToken cancellationToken = default);
    Task<Alarm?> SetEnabledAsync(Guid alarmId, bool isEnabled, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid alarmId, CancellationToken cancellationToken = default);
}
