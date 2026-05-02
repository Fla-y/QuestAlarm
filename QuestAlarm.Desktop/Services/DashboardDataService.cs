using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Infrastructure.Persistence;

namespace QuestAlarm.Desktop.Services;

public sealed class DashboardDataService
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmSessionRepository _sessionRepository;
    private readonly IAlarmDueEvaluator _dueEvaluator;
    private readonly StoragePaths _storagePaths;

    public DashboardDataService(
        IAlarmRepository alarmRepository,
        IAlarmSessionRepository sessionRepository,
        IAlarmDueEvaluator dueEvaluator,
        StoragePaths storagePaths)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _dueEvaluator = dueEvaluator ?? throw new ArgumentNullException(nameof(dueEvaluator));
        _storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync()
    {
        var alarms = (await _alarmRepository.GetAllAsync())
            .OrderBy(a => a.Schedule.Time)
            .ThenBy(a => a.Title)
            .ToList();

        var sessions = (await _sessionRepository.GetAllAsync())
            .OrderByDescending(s => s.TriggeredAtUtc)
            .ToList();

        var now = DateTime.Now;
        var nextAlarm = alarms
            .Where(a => a.IsEnabled)
            .Select(alarm => new
            {
                Alarm = alarm,
                Evaluation = _dueEvaluator.Evaluate(alarm, now)
            })
            .Where(item => item.Evaluation.Occurrence is not null)
            .OrderBy(item => item.Evaluation.Occurrence!.OccurrenceLocalDateTime)
            .ThenBy(item => item.Alarm.Title)
            .Select(item => item.Alarm)
            .FirstOrDefault();

        return new DashboardSnapshot(
            _storagePaths.RootDirectory,
            alarms.Count,
            alarms.Count(a => a.IsEnabled),
            sessions.Count,
            nextAlarm,
            alarms.Take(6).ToList(),
            sessions.Take(6).ToList());
    }
}
