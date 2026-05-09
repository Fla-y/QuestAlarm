using System.Diagnostics;

namespace QuestAlarm.Infrastructure.Persistence;

internal static class JsonCorruptFileQuarantine
{
    private const string CorruptDirectoryName = "Corrupt";

    public static void TryQuarantine(string filePath, Exception exception)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            var corruptDirectoryPath = Path.Combine(directoryPath, CorruptDirectoryName);
            Directory.CreateDirectory(corruptDirectoryPath);

            var corruptFilePath = BuildCorruptFilePath(corruptDirectoryPath, filePath);
            File.Move(filePath, corruptFilePath);

            Trace.TraceWarning(
                "QuestAlarm quarantined corrupt JSON file '{0}' to '{1}'. Error: {2}",
                filePath,
                corruptFilePath,
                exception.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Trace.TraceWarning(
                "QuestAlarm failed to quarantine corrupt JSON file '{0}'. Error: {1}",
                filePath,
                ex.Message);
        }
    }

    private static string BuildCorruptFilePath(string corruptDirectoryPath, string originalFilePath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFilePath);
        var extension = Path.GetExtension(originalFilePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var candidateFilePath = Path.Combine(
            corruptDirectoryPath,
            $"{fileNameWithoutExtension}.{timestamp}{extension}");

        if (!File.Exists(candidateFilePath))
        {
            return candidateFilePath;
        }

        return Path.Combine(
            corruptDirectoryPath,
            $"{fileNameWithoutExtension}.{timestamp}.{Guid.NewGuid():N}{extension}");
    }
}
