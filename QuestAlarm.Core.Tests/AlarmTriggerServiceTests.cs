using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Services;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Core.Tests;

[TestClass]
public sealed class AlarmTriggerServiceTests
{
    [TestMethod]
    public void TriggeringOneTimeAlarmDisablesSchedulingAndCreatesSession()
    {
        var alarm = new Alarm(
            Guid.NewGuid(),
            "Trigger test alarm",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                startDate: new DateOnly(2026, 5, 7),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);

        var trigger = new ScheduledAlarmTrigger(
            alarm,
            new AlarmOccurrence(alarm.Id, new DateTime(2026, 5, 7, 7, 30, 0)));
        var triggeredAtUtc = new DateTime(2026, 5, 7, 5, 30, 0, DateTimeKind.Utc);

        var session = new AlarmTriggerService().Trigger(trigger, triggeredAtUtc);

        Assert.IsFalse(alarm.IsEnabled);
        Assert.AreEqual(AlarmState.Triggering, alarm.State);
        Assert.AreEqual(alarm.Id, session.AlarmId);
        Assert.AreEqual(AlarmState.Triggering, session.State);
        Assert.AreEqual(triggeredAtUtc, session.TriggeredAtUtc);
        Assert.IsFalse(string.IsNullOrWhiteSpace(session.ChallengeToken));
    }

    [TestMethod]
    public void TriggeringRecurringAlarmKeepsSchedulingEnabled()
    {
        var alarm = new Alarm(
            Guid.NewGuid(),
            "Recurring trigger test alarm",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                isRecurring: true,
                recurringDays: [DayOfWeek.Monday]),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);

        var trigger = new ScheduledAlarmTrigger(
            alarm,
            new AlarmOccurrence(alarm.Id, new DateTime(2026, 5, 4, 7, 30, 0)));

        _ = new AlarmTriggerService().Trigger(trigger, DateTime.UtcNow);

        Assert.IsTrue(alarm.IsEnabled);
        Assert.AreEqual(AlarmState.Triggering, alarm.State);
    }
}
