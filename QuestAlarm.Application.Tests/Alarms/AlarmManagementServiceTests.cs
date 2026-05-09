using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Application.Alarms;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Application.Tests.Alarms;

[TestClass]
public sealed class AlarmManagementServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WithValidOneTimeAlarm_SavesScheduledAlarm()
    {
        var repository = new InMemoryAlarmRepository();
        var clock = new FakeClock(new DateTime(2026, 5, 7, 8, 30, 0, DateTimeKind.Utc));
        var service = new AlarmManagementService(repository, clock);

        var result = await service.CreateAsync(new CreateAlarmCommand(
            "Morning quest",
            "07:45",
            "2026-05-08",
            IsRecurring: false,
            RecurringDays: null));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Morning quest", result.Alarm!.Title);
        Assert.AreEqual(AlarmState.Scheduled, result.Alarm.State);
        Assert.IsTrue(result.Alarm.IsEnabled);
        Assert.AreEqual(clock.UtcNow, result.Alarm.CreatedAtUtc);
        Assert.AreEqual(1, repository.SavedCount);
    }

    [TestMethod]
    public async Task CreateAsync_WithRecurringAlarmWithoutDays_ReturnsValidationError()
    {
        var service = new AlarmManagementService(new InMemoryAlarmRepository(), new FakeClock());

        var result = await service.CreateAsync(new CreateAlarmCommand(
            "Weekday quest",
            "07:30",
            StartDate: null,
            IsRecurring: true,
            RecurringDays: Array.Empty<string>()));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Recurring alarms require at least one valid recurring day.", result.Error);
    }

    [TestMethod]
    public async Task UpdateAsync_WithRecurringAlarmAndStartDate_ReturnsValidationError()
    {
        var alarm = NewRecurringAlarm();
        var repository = new InMemoryAlarmRepository(alarm);
        var service = new AlarmManagementService(repository, new FakeClock());

        var result = await service.UpdateAsync(
            alarm.Id,
            new UpdateAlarmCommand(
                Title: null,
                Time: null,
                StartDate: "2026-05-08",
                RecurringDays: null));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Recurring alarms do not support startDate updates.", result.Error);
        Assert.AreEqual(0, repository.SavedCount);
    }

    [TestMethod]
    public async Task UpdateAsync_WithOneTimeAlarm_UpdatesTitleAndSchedule()
    {
        var alarm = NewOneTimeAlarm();
        var repository = new InMemoryAlarmRepository(alarm);
        var service = new AlarmManagementService(repository, new FakeClock());

        var result = await service.UpdateAsync(
            alarm.Id,
            new UpdateAlarmCommand(
                "Updated quest",
                "08:15",
                "2026-05-09",
                RecurringDays: null));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Updated quest", result.Alarm!.Title);
        Assert.AreEqual(new TimeOnly(8, 15), result.Alarm.Schedule.Time);
        Assert.AreEqual(new DateOnly(2026, 5, 9), result.Alarm.Schedule.StartDate);
        Assert.AreEqual(1, repository.SavedCount);
    }

    [TestMethod]
    public async Task SetEnabledAsync_WithExistingAlarm_DisablesAlarm()
    {
        var alarm = NewOneTimeAlarm();
        var repository = new InMemoryAlarmRepository(alarm);
        var service = new AlarmManagementService(repository, new FakeClock());

        var result = await service.SetEnabledAsync(alarm.Id, isEnabled: false);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual(AlarmState.Disabled, result.State);
        Assert.AreEqual(1, repository.SavedCount);
    }

    private static Alarm NewOneTimeAlarm()
    {
        return new Alarm(
            Guid.NewGuid(),
            "One-time",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                startDate: new DateOnly(2026, 5, 8),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }

    private static Alarm NewRecurringAlarm()
    {
        return new Alarm(
            Guid.NewGuid(),
            "Recurring",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                isRecurring: true,
                recurringDays: [DayOfWeek.Monday]),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock()
            : this(new DateTime(2026, 5, 7, 8, 30, 0, DateTimeKind.Utc))
        {
        }

        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
        public DateTime LocalNow => UtcNow.ToLocalTime();
    }

    private sealed class InMemoryAlarmRepository : IAlarmRepository
    {
        private readonly Dictionary<Guid, Alarm> _alarms = [];

        public InMemoryAlarmRepository(params Alarm[] alarms)
        {
            foreach (var alarm in alarms)
            {
                _alarms[alarm.Id] = alarm;
            }
        }

        public int SavedCount { get; private set; }

        public Task<IReadOnlyCollection<Alarm>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<Alarm>>(_alarms.Values.ToList());
        }

        public Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _alarms.TryGetValue(id, out var alarm);
            return Task.FromResult(alarm);
        }

        public Task SaveAsync(Alarm alarm, CancellationToken cancellationToken = default)
        {
            _alarms[alarm.Id] = alarm;
            SavedCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _alarms.Remove(id);
            return Task.CompletedTask;
        }
    }
}
