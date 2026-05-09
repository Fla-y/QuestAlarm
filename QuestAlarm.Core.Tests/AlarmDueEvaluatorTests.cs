using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Services;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Core.Tests;

[TestClass]
public sealed class AlarmDueEvaluatorTests
{
    private readonly AlarmDueEvaluator _evaluator = new(new AlarmOccurrenceCalculator());

    [TestMethod]
    public void OneTimeAlarmWithinGraceWindowIsDue()
    {
        var alarm = CreateOneTimeAlarm(
            new DateOnly(2026, 5, 7),
            new TimeOnly(7, 30));

        var result = _evaluator.Evaluate(alarm, new DateTime(2026, 5, 7, 7, 44, 0));

        Assert.AreEqual(AlarmDueStatus.Due, result.Status);
        Assert.AreEqual(new DateTime(2026, 5, 7, 7, 30, 0), result.Occurrence?.OccurrenceLocalDateTime);
    }

    [TestMethod]
    public void OneTimeAlarmOlderThanGraceWindowIsMissed()
    {
        var alarm = CreateOneTimeAlarm(
            new DateOnly(2026, 5, 7),
            new TimeOnly(7, 30));

        var result = _evaluator.Evaluate(alarm, new DateTime(2026, 5, 7, 7, 46, 0));

        Assert.AreEqual(AlarmDueStatus.Missed, result.Status);
        Assert.AreEqual(new DateTime(2026, 5, 7, 7, 30, 0), result.Occurrence?.OccurrenceLocalDateTime);
    }

    [TestMethod]
    public void RecurringAlarmWithinGraceWindowIsDue()
    {
        var alarm = CreateRecurringAlarm(
            new TimeOnly(7, 30),
            DayOfWeek.Monday);

        var result = _evaluator.Evaluate(alarm, new DateTime(2026, 5, 4, 7, 44, 0));

        Assert.AreEqual(AlarmDueStatus.Due, result.Status);
        Assert.AreEqual(new DateTime(2026, 5, 4, 7, 30, 0), result.Occurrence?.OccurrenceLocalDateTime);
    }

    [TestMethod]
    public void RecurringAlarmOlderThanGraceWindowSkipsToNextFutureOccurrence()
    {
        var alarm = CreateRecurringAlarm(
            new TimeOnly(7, 30),
            DayOfWeek.Monday);

        var result = _evaluator.Evaluate(alarm, new DateTime(2026, 5, 4, 7, 46, 0));

        Assert.AreEqual(AlarmDueStatus.NotDueYet, result.Status);
        Assert.AreEqual(new DateTime(2026, 5, 11, 7, 30, 0), result.Occurrence?.OccurrenceLocalDateTime);
    }

    private static Alarm CreateOneTimeAlarm(DateOnly date, TimeOnly time)
    {
        return new Alarm(
            Guid.NewGuid(),
            "One-time test alarm",
            new AlarmSchedule(time, startDate: date, isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }

    private static Alarm CreateRecurringAlarm(TimeOnly time, params DayOfWeek[] days)
    {
        return new Alarm(
            Guid.NewGuid(),
            "Recurring test alarm",
            new AlarmSchedule(time, isRecurring: true, recurringDays: days),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }
}
