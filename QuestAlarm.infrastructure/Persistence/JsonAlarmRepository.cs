using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.ValueObjects;
using QuestAlarm.Infrastructure.Persistence.Models;
using System.Text.Json;

namespace QuestAlarm.Infrastructure.Persistence;

public sealed class JsonAlarmRepository : IAlarmRepository
{
    private readonly string _alarmsDirectoryPath;

    public JsonAlarmRepository(string alarmsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(alarmsDirectoryPath))
        {
            throw new ArgumentException("Alarms directory path cannot be null or empty.", nameof(alarmsDirectoryPath));
        }

        _alarmsDirectoryPath = alarmsDirectoryPath;
        Directory.CreateDirectory(_alarmsDirectoryPath);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    public async Task<IReadOnlyCollection<Alarm>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var alarmFiles = Directory.GetFiles(_alarmsDirectoryPath, "*.json");
        var alarms = new List<Alarm>();

        foreach (var filePath in alarmFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var alarm = await TryReadAlarmAsync(filePath, cancellationToken);

            if (alarm is null)
            {
                continue;
            }

            alarms.Add(alarm);
        }

        return alarms;
    }

    public async Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = GetAlarmFilePath(id);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await TryReadAlarmAsync(filePath, cancellationToken);
    }

    public async Task SaveAsync(Alarm alarm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        var fileModel = ToFileModel(alarm);
        var filePath = GetAlarmFilePath(alarm.Id);

        var json = JsonSerializer.Serialize(fileModel, SerializerOptions);

        await JsonFilePersistence.WriteAllTextAtomicallyAsync(filePath, json, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filePath = GetAlarmFilePath(id);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string GetAlarmFilePath(Guid alarmId)
    {
        return Path.Combine(_alarmsDirectoryPath, $"{alarmId}.json");
    }

    private static async Task<Alarm?> TryReadAlarmAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var fileModel = JsonSerializer.Deserialize<AlarmFileModel>(json, SerializerOptions);

            return fileModel is null
                ? null
                : ToDomainModel(fileModel);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static AlarmFileModel ToFileModel(Alarm alarm)
    {
        return new AlarmFileModel
        {
            Id = alarm.Id,
            Title = alarm.Title,
            Hour = alarm.Schedule.Time.Hour,
            Minute = alarm.Schedule.Time.Minute,
            StartDate = alarm.Schedule.StartDate,
            IsRecurring = alarm.Schedule.IsRecurring,
            RecurringDays = [.. alarm.Schedule.RecurringDays],
            IsEnabled = alarm.IsEnabled,
            State = (int)alarm.State,
            CreatedAtUtc = alarm.CreatedAtUtc
        };
    }

    private static Alarm ToDomainModel(AlarmFileModel fileModel)
    {
        var schedule = new AlarmSchedule(
            new TimeOnly(fileModel.Hour, fileModel.Minute),
            fileModel.StartDate,
            fileModel.IsRecurring,
            fileModel.RecurringDays);

        return new Alarm(
            fileModel.Id,
            fileModel.Title,
            schedule,
            fileModel.IsEnabled,
            (AlarmState)fileModel.State,
            fileModel.CreatedAtUtc);
    }
}
