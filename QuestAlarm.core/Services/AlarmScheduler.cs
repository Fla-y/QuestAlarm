using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmScheduler : IAlarmScheduler
{
    private readonly IAlarmDueEvaluator _dueEvaluator;
    private readonly HashSet<string> _triggeredOccurrences = [];

    public AlarmScheduler(IAlarmDueEvaluator dueEvaluator)
    {
        _dueEvaluator = dueEvaluator ?? throw new ArgumentNullException(nameof(dueEvaluator));
    }

    public IReadOnlyCollection<ScheduledAlarmTrigger> GetDueAlarms(
        IEnumerable<Alarm> alarms,
        DateTime referenceLocalDateTime)
    {
        ArgumentNullException.ThrowIfNull(alarms);

        var dueTriggers = new List<ScheduledAlarmTrigger>();

        foreach (var alarm in alarms)
        {
            var evaluation = _dueEvaluator.Evaluate(alarm, referenceLocalDateTime);

            if (evaluation.Status != AlarmDueStatus.Due || evaluation.Occurrence is null)
            {
                continue;
            }

            var occurrenceKey = BuildOccurrenceKey(
                evaluation.Occurrence.AlarmId,
                evaluation.Occurrence.OccurrenceLocalDateTime);

            if (_triggeredOccurrences.Contains(occurrenceKey))
            {
                continue;
            }

            _triggeredOccurrences.Add(occurrenceKey);
            dueTriggers.Add(new ScheduledAlarmTrigger(alarm, evaluation.Occurrence));
        }

        return dueTriggers;
    }

    private static string BuildOccurrenceKey(Guid alarmId, DateTime occurrenceLocalDateTime)
    {
        return $"{alarmId:N}_{occurrenceLocalDateTime:O}";
    }
}