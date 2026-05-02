using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmRepository
{
    Task<IReadOnlyCollection<Alarm>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(Alarm alarm, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}