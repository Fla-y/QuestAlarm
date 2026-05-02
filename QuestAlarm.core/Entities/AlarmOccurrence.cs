namespace QuestAlarm.Core.Entities;

public sealed class AlarmOccurrence
{
    public Guid AlarmId { get; }
    public DateTime OccurrenceLocalDateTime { get; }

    public AlarmOccurrence(Guid alarmId, DateTime occurrenceLocalDateTime)
    {
        AlarmId = alarmId;
        OccurrenceLocalDateTime = occurrenceLocalDateTime;
    }
}