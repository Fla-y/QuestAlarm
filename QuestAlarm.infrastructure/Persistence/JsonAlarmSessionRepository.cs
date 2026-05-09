using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Infrastructure.Persistence.Models;
using System.Text.Json;

namespace QuestAlarm.Infrastructure.Persistence;

public sealed class JsonAlarmSessionRepository : IAlarmSessionRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _sessionsDirectoryPath;

    public JsonAlarmSessionRepository(string sessionsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(sessionsDirectoryPath))
        {
            throw new ArgumentException("Sessions directory path cannot be null or empty.", nameof(sessionsDirectoryPath));
        }

        _sessionsDirectoryPath = sessionsDirectoryPath;
        Directory.CreateDirectory(_sessionsDirectoryPath);
    }

    public async Task<IReadOnlyCollection<AlarmSession>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sessionFiles = Directory.GetFiles(_sessionsDirectoryPath, "*.json");
        var sessions = new List<AlarmSession>();

        foreach (var filePath in sessionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = await ReadSessionOrQuarantineAsync(filePath, cancellationToken);

            if (session is null)
            {
                continue;
            }

            sessions.Add(session);
        }

        return sessions;
    }

    public async Task<AlarmSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = GetSessionFilePath(id);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await ReadSessionOrQuarantineAsync(filePath, cancellationToken);
    }

    public async Task SaveAsync(AlarmSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var fileModel = ToFileModel(session);
        var filePath = GetSessionFilePath(session.Id);
        var json = JsonSerializer.Serialize(fileModel, SerializerOptions);

        await JsonFilePersistence.WriteAllTextAtomicallyAsync(filePath, json, cancellationToken);
    }

    private string GetSessionFilePath(Guid sessionId)
    {
        return Path.Combine(_sessionsDirectoryPath, $"{sessionId}.json");
    }

    private static async Task<AlarmSession?> ReadSessionOrQuarantineAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var fileModel = JsonSerializer.Deserialize<AlarmSessionFileModel>(json, SerializerOptions);

            return fileModel is null
                ? null
                : ToDomainModel(fileModel);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            JsonCorruptFileQuarantine.TryQuarantine(filePath, ex);
            return null;
        }
    }

    private static AlarmSessionFileModel ToFileModel(AlarmSession session)
    {
        return new AlarmSessionFileModel
        {
            Id = session.Id,
            AlarmId = session.AlarmId,
            TriggeredAtUtc = session.TriggeredAtUtc,
            State = (int)session.State,
            CompletedAtUtc = session.CompletedAtUtc,
            ChallengeToken = session.ChallengeToken
        };
    }

    private static AlarmSession ToDomainModel(AlarmSessionFileModel fileModel)
    {
        return new AlarmSession(
            fileModel.Id,
            fileModel.AlarmId,
            fileModel.TriggeredAtUtc,
            (AlarmState)fileModel.State,
            fileModel.CompletedAtUtc,
            fileModel.ChallengeToken);
    }
}
