using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmMissedService : IAlarmMissedService
{
    public void MarkMissed(Alarm alarm)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        if (alarm.Schedule.IsRecurring)
        {
            return;
        }

        alarm.MarkMissed();
    }
}
