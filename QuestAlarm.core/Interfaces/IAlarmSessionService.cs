using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IAlarmSessionService
{
    void StartChallenge(AlarmSession session, Alarm alarm);
    void CompleteChallenge(AlarmSession session, Alarm alarm, DateTime completedAtUtc);
    void FailChallenge(AlarmSession session, Alarm alarm);
}
