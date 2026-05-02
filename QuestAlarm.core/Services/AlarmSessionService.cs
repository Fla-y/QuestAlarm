using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmSessionService : IAlarmSessionService
{
    public void StartChallenge(AlarmSession session, Alarm alarm)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(alarm);

        ValidateSessionAlarm(session, alarm);

        if (IsFinal(session.State))
        {
            throw new InvalidOperationException($"Session '{session.Id}' is already {session.State}.");
        }

        session.MarkChallengeRunning();
        alarm.MarkChallengeRunning();
    }

    public void CompleteChallenge(AlarmSession session, Alarm alarm, DateTime completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(alarm);

        ValidateSessionAlarm(session, alarm);

        if (session.State == QuestAlarm.Core.Enums.AlarmState.Completed)
        {
            alarm.MarkCompleted();
            return;
        }

        if (IsFinal(session.State))
        {
            throw new InvalidOperationException($"Session '{session.Id}' is already {session.State}.");
        }

        session.MarkCompleted(completedAtUtc);
        alarm.MarkCompleted();
    }

    public void FailChallenge(AlarmSession session, Alarm alarm)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(alarm);

        ValidateSessionAlarm(session, alarm);

        if (session.State == QuestAlarm.Core.Enums.AlarmState.Failed)
        {
            alarm.MarkFailed();
            return;
        }

        if (IsFinal(session.State))
        {
            throw new InvalidOperationException($"Session '{session.Id}' is already {session.State}.");
        }

        session.MarkFailed();
        alarm.MarkFailed();
    }

    private static void ValidateSessionAlarm(AlarmSession session, Alarm alarm)
    {
        if (session.AlarmId != alarm.Id)
        {
            throw new InvalidOperationException($"Session '{session.Id}' does not belong to alarm '{alarm.Id}'.");
        }
    }

    private static bool IsFinal(QuestAlarm.Core.Enums.AlarmState state)
    {
        return state is QuestAlarm.Core.Enums.AlarmState.Completed or QuestAlarm.Core.Enums.AlarmState.Failed;
    }
}
