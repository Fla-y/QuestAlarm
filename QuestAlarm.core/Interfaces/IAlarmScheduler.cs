using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmScheduler
{
    IReadOnlyCollection<ScheduledAlarmTrigger> GetDueAlarms(
        IEnumerable<Alarm> alarms,
        DateTime referenceLocalDateTime);
}