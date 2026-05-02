using QuestAlarm.Infrastructure.ChallengeClient;
using System.IO;
using System.Text.Json;

namespace QuestAlarm.Desktop.Services;

public sealed class DesktopSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public DesktopSettingsService()
    {
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    public async Task<DesktopSettingsModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return DesktopSettingsModel.CreateDefault(_settingsFilePath);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<DesktopSettingsModel>(json, SerializerOptions)
                ?? DesktopSettingsModel.CreateDefault(_settingsFilePath);

            return settings with { SettingsFilePath = _settingsFilePath };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return DesktopSettingsModel.CreateDefault(_settingsFilePath);
        }
    }

    public async Task SaveAsync(DesktopSettingsModel settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(
            settings with { SettingsFilePath = null },
            SerializerOptions);

        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
    }
}

public sealed record DesktopSettingsModel(
    StorageSettings Storage,
    DevelopmentSettings Development,
    ChallengeClientOptions ChallengeClient,
    string? SettingsFilePath = null)
{
    public static DesktopSettingsModel CreateDefault(string settingsFilePath)
    {
        return new DesktopSettingsModel(
            new StorageSettings("%LOCALAPPDATA%\\QuestAlarm"),
            new DevelopmentSettings(ShowTestAlarmMenuOption: true),
            new ChallengeClientOptions
            {
                ExecutablePath = "%LOCALAPPDATA%\\QuestAlarm\\Tools\\FakeChallengeClient\\QuestAlarm.FakeChallengeClient.exe",
                ArgumentsTemplate = "--session-id {SessionId} --alarm-id {AlarmId} --callback-url {CallbackUrl} --challenge-token {ChallengeToken}",
                CallbackUrl = "http://localhost:5055",
                WorkingDirectory = "%LOCALAPPDATA%\\QuestAlarm\\Tools\\FakeChallengeClient"
            },
            settingsFilePath);
    }
}

public sealed record StorageSettings(string RootDirectory);

public sealed record DevelopmentSettings(bool ShowTestAlarmMenuOption);
