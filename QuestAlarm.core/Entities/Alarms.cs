using QuestAlarm.Core.Enums;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Core.Entities;

public sealed class Alarm
{
    public Guid Id { get; private set; }
    public string Title { get; private set; }
    public AlarmSchedule Schedule { get; private set; }
    public bool IsEnabled { get; private set; }
    public AlarmState State { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Alarm(
        Guid id,
        string title,
        AlarmSchedule schedule,
        bool isEnabled,
        AlarmState state,
        DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Alarm title cannot be empty.", nameof(title));
        }

        Id = id;
        Title = title.Trim();
        Schedule = schedule;
        IsEnabled = isEnabled;
        State = state;
        CreatedAtUtc = createdAtUtc;
    }

    public void Enable()
    {
        IsEnabled = true;
        State = AlarmState.Scheduled;
    }

    public void Disable()
    {
        IsEnabled = false;
        State = AlarmState.Disabled;
    }

    public void DisableScheduling()
    {
        IsEnabled = false;
    }

    public void MarkTriggering()
    {
        State = AlarmState.Triggering;
    }

    public void MarkChallengeRunning()
    {
        State = AlarmState.ChallengeRunning;
    }

    public void MarkCompleted()
    {
        State = AlarmState.Completed;
    }

    public void MarkFailed()
    {
        State = AlarmState.Failed;
    }

    public void MarkMissed()
    {
        IsEnabled = false;
        State = AlarmState.Missed;
    }

    public void UpdateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Alarm title cannot be empty.", nameof(title));
        }

        Title = title.Trim();
    }

    public void UpdateSchedule(AlarmSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        Schedule = schedule;
    }
}
