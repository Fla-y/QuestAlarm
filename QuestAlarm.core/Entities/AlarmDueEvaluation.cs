using QuestAlarm.Core.Enums;

namespace QuestAlarm.Core.Entities;

public sealed class AlarmDueEvaluation
{
    public Guid AlarmId { get; }
    public AlarmDueStatus Status { get; }
    public AlarmOccurrence? Occurrence { get; }

    public AlarmDueEvaluation(Guid alarmId, AlarmDueStatus status, AlarmOccurrence? occurrence)
    {
        AlarmId = alarmId;
        Status = status;
        Occurrence = occurrence;
    }
}