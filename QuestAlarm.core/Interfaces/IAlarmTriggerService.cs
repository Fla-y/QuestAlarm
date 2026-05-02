using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmTriggerService
{
    AlarmSession Trigger(ScheduledAlarmTrigger trigger, DateTime triggeredAtUtc);
}
