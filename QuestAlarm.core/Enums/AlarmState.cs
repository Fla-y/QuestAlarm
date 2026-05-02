namespace QuestAlarm.Core.Enums;

public enum AlarmState
{
    Scheduled = 0,
    Triggering = 1,
    ChallengeRunning = 2,
    Snoozed = 3,
    Completed = 4,
    Failed = 5,
    Missed = 6,
    Disabled = 7
}