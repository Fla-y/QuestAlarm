using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Infrastructure.Persistence;

namespace QuestAlarm.Infrastructure.Tests;

[TestClass]
public sealed class JsonAlarmSessionRepositoryTests
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
    public async Task GetAllSkipsAndQuarantinesCorruptSessionFiles()
    {
        var repository = new JsonAlarmSessionRepository(_tempDirectory!);
        var validSession = CreateSession();
        var corruptSessionFilePath = Path.Combine(_tempDirectory!, $"{Guid.NewGuid()}.json");

        await repository.SaveAsync(validSession);
        await File.WriteAllTextAsync(corruptSessionFilePath, "{ invalid json");

        var sessions = await repository.GetAllAsync();

        Assert.AreEqual(1, sessions.Count);
        Assert.AreEqual(validSession.Id, sessions.Single().Id);
        Assert.IsFalse(File.Exists(corruptSessionFilePath));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_tempDirectory!, "Corrupt"), "*.json").Length);
    }

    [TestMethod]
    public async Task GetByIdReturnsNullAndQuarantinesCorruptSessionFile()
    {
        var repository = new JsonAlarmSessionRepository(_tempDirectory!);
        var sessionId = Guid.NewGuid();
        var corruptSessionFilePath = Path.Combine(_tempDirectory!, $"{sessionId}.json");
        await File.WriteAllTextAsync(corruptSessionFilePath, "{ invalid json");

        var session = await repository.GetByIdAsync(sessionId);

        Assert.IsNull(session);
        Assert.IsFalse(File.Exists(corruptSessionFilePath));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_tempDirectory!, "Corrupt"), "*.json").Length);
    }

    private static AlarmSession CreateSession()
    {
        return new AlarmSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            AlarmState.Triggering,
            challengeToken: "test-token");
    }
}
