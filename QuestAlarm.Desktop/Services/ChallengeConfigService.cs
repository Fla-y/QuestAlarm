using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace QuestAlarm.Desktop.Services;

public sealed class ChallengeConfigService
{
    private const string DictionaryFileName = "challenge-words.json";
    private const string ChallengeConfigFileName = "challenge-config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<string> CreateConfigAsync(
        string storageRootDirectory,
        AlarmSession session,
        Alarm alarm,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRootDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(alarm);

        var dictionary = await LoadDictionaryAsync(cancellationToken);
        var phrase = GeneratePhrase(dictionary, alarm.ChallengeDifficulty);
        var config = new ChallengeGameplayConfig(
            phrase,
            alarm.ChallengeDifficulty.ToString());

        var sessionDirectory = Path.Combine(
            Environment.ExpandEnvironmentVariables(storageRootDirectory),
            "ChallengeSessions",
            session.Id.ToString("N"));

        Directory.CreateDirectory(sessionDirectory);

        var configPath = Path.Combine(sessionDirectory, ChallengeConfigFileName);
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        await File.WriteAllTextAsync(configPath, json, cancellationToken);

        return configPath;
    }

    private static async Task<ChallengeWordDictionary> LoadDictionaryAsync(CancellationToken cancellationToken)
    {
        var dictionaryPath = Path.Combine(AppContext.BaseDirectory, DictionaryFileName);

        if (!File.Exists(dictionaryPath))
        {
            return ChallengeWordDictionary.CreateFallback();
        }

        try
        {
            var json = await File.ReadAllTextAsync(dictionaryPath, cancellationToken);
            return JsonSerializer.Deserialize<ChallengeWordDictionary>(json, SerializerOptions)
                ?? ChallengeWordDictionary.CreateFallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ChallengeWordDictionary.CreateFallback();
        }
    }

    private static string GeneratePhrase(
        ChallengeWordDictionary dictionary,
        ChallengeDifficulty difficulty)
    {
        var words = dictionary.GetWords(difficulty);
        var wordCount = dictionary.GetWordCount(difficulty);

        if (words.Count == 0)
        {
            words = ChallengeWordDictionary.CreateFallback().GetWords(difficulty);
        }

        var selectedWords = new List<string>(wordCount);

        for (var index = 0; index < wordCount; index++)
        {
            selectedWords.Add(words[RandomNumberGenerator.GetInt32(words.Count)]);
        }

        return string.Join(' ', selectedWords);
    }

    private sealed record ChallengeGameplayConfig(string Phrase, string Difficulty);

    private sealed class ChallengeWordDictionary
    {
        public Dictionary<string, int> WordCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Words { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int GetWordCount(ChallengeDifficulty difficulty)
        {
            return WordCounts.TryGetValue(difficulty.ToString(), out var count) && count > 0
                ? count
                : difficulty switch
                {
                    ChallengeDifficulty.Easy => 4,
                    ChallengeDifficulty.Hard => 10,
                    _ => 7
                };
        }

        public List<string> GetWords(ChallengeDifficulty difficulty)
        {
            return Words.TryGetValue(difficulty.ToString(), out var words)
                ? words.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList()
                : [];
        }

        public static ChallengeWordDictionary CreateFallback()
        {
            return new ChallengeWordDictionary
            {
                WordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Easy"] = 4,
                    ["Normal"] = 7,
                    ["Hard"] = 10
                },
                Words = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Easy"] = ["wake", "focus", "start", "type", "light", "quest"],
                    ["Normal"] = ["alarm", "morning", "energy", "steady", "typing", "awake", "challenge", "rhythm"],
                    ["Hard"] = ["discipline", "momentum", "precision", "attention", "keyboard", "activation", "resilience", "sequence"]
                }
            };
        }
    }
}
