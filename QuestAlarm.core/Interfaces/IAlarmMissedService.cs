using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmMissedService
{
    void MarkMissed(Alarm alarm);
}
