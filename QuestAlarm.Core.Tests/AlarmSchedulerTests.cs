using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Services;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Core.Tests;

[TestClass]
public sealed class AlarmSchedulerTests
{
    [TestMethod]
    public void SchedulerDoesNotReturnSameOccurrenceTwiceInSameRun()
    {
        var scheduler = new AlarmScheduler(
            new AlarmDueEvaluator(new AlarmOccurrenceCalculator()));

        var alarm = new Alarm(
            Guid.NewGuid(),
            "Dedup test alarm",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                startDate: new DateOnly(2026, 5, 7),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);

        var firstResult = scheduler.GetDueAlarms([alarm], new DateTime(2026, 5, 7, 7, 31, 0));
        var secondResult = scheduler.GetDueAlarms([alarm], new DateTime(2026, 5, 7, 7, 32, 0));

        Assert.AreEqual(1, firstResult.Count);
        Assert.AreEqual(0, secondResult.Count);
    }
}
