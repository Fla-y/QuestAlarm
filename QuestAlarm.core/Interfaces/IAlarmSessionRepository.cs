using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmSessionRepository
{
    Task<IReadOnlyCollection<AlarmSession>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AlarmSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(AlarmSession session, CancellationToken cancellationToken = default);
}
