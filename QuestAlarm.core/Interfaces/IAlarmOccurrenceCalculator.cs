using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmOccurrenceCalculator
{
    AlarmOccurrence? CalculateNextOccurrence(Alarm alarm, DateTime referenceLocalDateTime);
}
