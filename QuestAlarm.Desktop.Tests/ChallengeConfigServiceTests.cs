using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.ValueObjects;
using QuestAlarm.Desktop.Services;

namespace QuestAlarm.Desktop.Tests;

[TestClass]
public sealed class ChallengeConfigServiceTests
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
    public async Task CreateConfigAsync_WritesGameplayConfigUnderSessionDirectory()
    {
        var service = new ChallengeConfigService();
        var alarm = new Alarm(
            Guid.NewGuid(),
            "Morning alarm",
            new AlarmSchedule(
                new TimeOnly(7, 30),
                startDate: new DateOnly(2026, 5, 16),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow,
            challengeType: ChallengeType.Typing,
            challengeDifficulty: ChallengeDifficulty.Hard);
        var session = new AlarmSession(
            Guid.NewGuid(),
            alarm.Id,
            DateTime.UtcNow,
            AlarmState.Triggering,
            challengeToken: "token");

        var configPath = await service.CreateConfigAsync(_tempDirectory!, session, alarm);

        var expectedDirectory = Path.Combine(
            _tempDirectory!,
            "ChallengeSessions",
            session.Id.ToString("N"));

        Assert.AreEqual(Path.Combine(expectedDirectory, "challenge-config.json"), configPath);
        Assert.IsTrue(File.Exists(configPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert.AreEqual("Hard", document.RootElement.GetProperty("difficulty").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("phrase").GetString()));
    }
}
