using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.ValueObjects;
using QuestAlarm.Infrastructure.Persistence;

namespace QuestAlarm.Infrastructure.Tests;

[TestClass]
public sealed class JsonAlarmRepositoryTests
{
    private string? _tempDirectory;

    [TestInitialize]
    public void Initialize()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "QuestAlarm.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempDirectory is not null && Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetAllSkipsAndQuarantinesCorruptAlarmFiles()
    {
        var repository = new JsonAlarmRepository(_tempDirectory!);
        var validAlarm = CreateAlarm("Valid alarm");
        var corruptAlarmFilePath = Path.Combine(_tempDirectory!, $"{Guid.NewGuid()}.json");

        await repository.SaveAsync(validAlarm);
        await File.WriteAllTextAsync(corruptAlarmFilePath, "{ invalid json");

        var alarms = await repository.GetAllAsync();

        Assert.AreEqual(1, alarms.Count);
        Assert.AreEqual(validAlarm.Id, alarms.Single().Id);
        Assert.IsFalse(File.Exists(corruptAlarmFilePath));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_tempDirectory!, "Corrupt"), "*.json").Length);
    }

    [TestMethod]
    public async Task GetByIdReturnsNullAndQuarantinesCorruptAlarmFile()
    {
        var repository = new JsonAlarmRepository(_tempDirectory!);
        var alarmId = Guid.NewGuid();
        var corruptAlarmFilePath = Path.Combine(_tempDirectory!, $"{alarmId}.json");
        await File.WriteAllTextAsync(corruptAlarmFilePath, "{ invalid json");

        var alarm = await repository.GetByIdAsync(alarmId);

        Assert.IsNull(alarm);
        Assert.IsFalse(File.Exists(corruptAlarmFilePath));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_tempDirectory!, "Corrupt"), "*.json").Length);
    }

    private static Alarm CreateAlarm(string title)
    {
        return new Alarm(
            Guid.NewGuid(),
            title,
            new AlarmSchedule(
                new TimeOnly(7, 30),
                startDate: new DateOnly(2026, 5, 7),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }
}
