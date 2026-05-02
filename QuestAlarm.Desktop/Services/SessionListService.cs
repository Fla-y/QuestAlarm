using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Desktop.Services;

public sealed class SessionListService
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmSessionRepository _sessionRepository;

    public SessionListService(
        IAlarmRepository alarmRepository,
        IAlarmSessionRepository sessionRepository)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    public async Task<IReadOnlyList<SessionListItem>> GetRecentSessionsAsync(CancellationToken cancellationToken = default)
    {
        var alarms = await _alarmRepository.GetAllAsync(cancellationToken);
        var alarmsById = alarms.ToDictionary(alarm => alarm.Id);

        var sessions = await _sessionRepository.GetAllAsync(cancellationToken);

        return sessions
            .OrderByDescending(session => session.TriggeredAtUtc)
            .Select(session =>
            {
                alarmsById.TryGetValue(session.AlarmId, out var alarm);
                return new SessionListItem(session, alarm?.Title ?? "Deleted alarm", alarm);
            })
            .ToList();
    }
}

public sealed record SessionListItem(
    AlarmSession Session,
    string AlarmTitle,
    Alarm? Alarm);
