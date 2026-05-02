using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmTriggerService : IAlarmTriggerService
{
    public AlarmSession Trigger(ScheduledAlarmTrigger trigger, DateTime triggeredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var alarm = trigger.Alarm;
        alarm.MarkTriggering();

        var session = new AlarmSession(
            Guid.NewGuid(),
            alarm.Id,
            triggeredAtUtc,
            AlarmState.Triggering,
            challengeToken: ChallengeTokenGenerator.CreateToken());

        if (!alarm.Schedule.IsRecurring)
        {
            alarm.DisableScheduling();
        }

        return session;
    }
}
