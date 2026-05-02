namespace QuestAlarm.Core.ValueObjects;

public sealed record AlarmSchedule
{
    public TimeOnly Time { get; init; }
    public DateOnly? StartDate { get; init; }
    public bool IsRecurring { get; init; }
    public IReadOnlyCollection<DayOfWeek> RecurringDays { get; init; }

    public AlarmSchedule(
        TimeOnly time,
        DateOnly? startDate = null,
        bool isRecurring = false,
        IReadOnlyCollection<DayOfWeek>? recurringDays = null)
    {
        Time = time;
        StartDate = startDate;
        IsRecurring = isRecurring;
        RecurringDays = recurringDays ?? Array.Empty<DayOfWeek>();
    }
}