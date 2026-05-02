using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmDueEvaluator
{
    AlarmDueEvaluation Evaluate(Alarm alarm, DateTime referenceLocalDateTime);
}