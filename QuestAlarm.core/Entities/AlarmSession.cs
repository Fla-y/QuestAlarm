using QuestAlarm.Core.Enums;

namespace QuestAlarm.Core.Entities;

public sealed class AlarmSession
{
    public Guid Id { get; private set; }
    public Guid AlarmId { get; private set; }
    public DateTime TriggeredAtUtc { get; private set; }
    public AlarmState State { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string ChallengeToken { get; private set; }

    public AlarmSession(
        Guid id,
        Guid alarmId,
        DateTime triggeredAtUtc,
        AlarmState state,
        DateTime? completedAtUtc = null,
        string? challengeToken = null)
    {
        Id = id;
        AlarmId = alarmId;
        TriggeredAtUtc = triggeredAtUtc;
        State = state;
        CompletedAtUtc = completedAtUtc;
        ChallengeToken = challengeToken ?? string.Empty;
    }

    public void MarkChallengeRunning()
    {
        State = AlarmState.ChallengeRunning;
    }

    public void MarkCompleted(DateTime completedAtUtc)
    {
        State = AlarmState.Completed;
        CompletedAtUtc = completedAtUtc;
    }

    public void MarkFailed()
    {
        State = AlarmState.Failed;
    }

    public void MarkSnoozed()
    {
        State = AlarmState.Snoozed;
    }
}
