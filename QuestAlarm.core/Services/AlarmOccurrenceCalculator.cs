using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmOccurrenceCalculator : IAlarmOccurrenceCalculator
{
    public AlarmOccurrence? CalculateNextOccurrence(Alarm alarm, DateTime referenceLocalDateTime)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        if (!alarm.IsEnabled)
        {
            return null;
        }

        return alarm.Schedule.IsRecurring
            ? CalculateRecurringOccurrence(alarm, referenceLocalDateTime)
            : CalculateOneTimeOccurrence(alarm, referenceLocalDateTime);
    }

    private static AlarmOccurrence? CalculateOneTimeOccurrence(Alarm alarm, DateTime referenceLocalDateTime)
    {
        if (alarm.Schedule.StartDate is null)
        {
            return null;
        }

        var occurrence = alarm.Schedule.StartDate.Value.ToDateTime(alarm.Schedule.Time);

        if (occurrence < referenceLocalDateTime)
        {
            return null;
        }

        return new AlarmOccurrence(alarm.Id, occurrence);
    }

    private static AlarmOccurrence? CalculateRecurringOccurrence(Alarm alarm, DateTime referenceLocalDateTime)
    {
        var recurringDays = alarm.Schedule.RecurringDays;

        if (recurringDays.Count == 0)
        {
            return null;
        }

        var referenceDate = DateOnly.FromDateTime(referenceLocalDateTime);

        for (var offset = 0; offset < 7; offset++)
        {
            var candidateDate = referenceDate.AddDays(offset);

            if (!recurringDays.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            var candidateDateTime = candidateDate.ToDateTime(alarm.Schedule.Time);

            return new AlarmOccurrence(alarm.Id, candidateDateTime);
        }

        return null;
    }
}
