namespace QuestAlarm.Core.Entities;

public sealed class ScheduledAlarmTrigger
{
    public Alarm Alarm { get; }
    public AlarmOccurrence Occurrence { get; }

    public ScheduledAlarmTrigger(Alarm alarm, AlarmOccurrence occurrence)
    {
        Alarm = alarm ?? throw new ArgumentNullException(nameof(alarm));
        Occurrence = occurrence ?? throw new ArgumentNullException(nameof(occurrence));
    }
}