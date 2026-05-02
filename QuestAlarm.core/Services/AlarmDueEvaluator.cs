using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Core.Services;

public sealed class AlarmDueEvaluator : IAlarmDueEvaluator
{
    private static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(15);

    private readonly IAlarmOccurrenceCalculator _occurrenceCalculator;

    public AlarmDueEvaluator(IAlarmOccurrenceCalculator occurrenceCalculator)
    {
        _occurrenceCalculator = occurrenceCalculator ?? throw new ArgumentNullException(nameof(occurrenceCalculator));
    }

    public AlarmDueEvaluation Evaluate(Alarm alarm, DateTime referenceLocalDateTime)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        if (!alarm.IsEnabled)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotScheduled, null);
        }

        if (alarm.Schedule.IsRecurring)
        {
            return EvaluateRecurringAlarm(alarm, referenceLocalDateTime);
        }

        return EvaluateOneTimeAlarm(alarm, referenceLocalDateTime);
    }

    private AlarmDueEvaluation EvaluateRecurringAlarm(Alarm alarm, DateTime referenceLocalDateTime)
    {
        var todayOccurrence = _occurrenceCalculator.CalculateNextOccurrence(
            alarm,
            referenceLocalDateTime.Date);

        if (todayOccurrence is null)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotScheduled, null);
        }

        if (todayOccurrence.OccurrenceLocalDateTime > referenceLocalDateTime)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotDueYet, todayOccurrence);
        }

        var delay = referenceLocalDateTime - todayOccurrence.OccurrenceLocalDateTime;

        if (delay <= GraceWindow)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.Due, todayOccurrence);
        }

        var nextFutureOccurrence = _occurrenceCalculator.CalculateNextOccurrence(
            alarm,
            referenceLocalDateTime.Date.AddDays(1));

        return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotDueYet, nextFutureOccurrence);
    }

    private AlarmDueEvaluation EvaluateOneTimeAlarm(Alarm alarm, DateTime referenceLocalDateTime)
    {
        var occurrence = _occurrenceCalculator.CalculateNextOccurrence(alarm, referenceLocalDateTime);

        if (occurrence is null)
        {
            return EvaluatePastOneTimeAlarm(alarm, referenceLocalDateTime);
        }

        if (occurrence.OccurrenceLocalDateTime > referenceLocalDateTime)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotDueYet, occurrence);
        }

        var delay = referenceLocalDateTime - occurrence.OccurrenceLocalDateTime;

        if (delay <= GraceWindow)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.Due, occurrence);
        }

        return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.Missed, occurrence);
    }

    private static AlarmDueEvaluation EvaluatePastOneTimeAlarm(Alarm alarm, DateTime referenceLocalDateTime)
    {
        if (alarm.Schedule.StartDate is null)
        {
            return new AlarmDueEvaluation(alarm.Id, AlarmDueStatus.NotScheduled, null);
        }

        var scheduledDateTime = alarm.Schedule.StartDate.Value.ToDateTime(alarm.Schedule.Time);

        if (scheduledDateTime > referenceLocalDateTime)
        {
            return new AlarmDueEvaluation(
                alarm.Id,
                AlarmDueStatus.NotDueYet,
                new AlarmOccurrence(alarm.Id, scheduledDateTime));
        }

        var delay = referenceLocalDateTime - scheduledDateTime;

        if (delay <= GraceWindow)
        {
            return new AlarmDueEvaluation(
                alarm.Id,
                AlarmDueStatus.Due,
                new AlarmOccurrence(alarm.Id, scheduledDateTime));
        }

        return new AlarmDueEvaluation(
            alarm.Id,
            AlarmDueStatus.Missed,
            new AlarmOccurrence(alarm.Id, scheduledDateTime));
    }
}