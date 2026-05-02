namespace QuestAlarm.Infrastructure.Persistence;

internal static class JsonFilePersistence
{
    public static async Task WriteAllTextAtomicallyAsync(
        string filePath,
        string contents,
        CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(filePath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("File path must include a directory.", nameof(filePath));
        }

        Directory.CreateDirectory(directoryPath);

        var tempFilePath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempFilePath, contents, cancellationToken);

            if (File.Exists(filePath))
            {
                File.Replace(tempFilePath, filePath, destinationBackupFileName: null);
                return;
            }

            File.Move(tempFilePath, filePath);
        }
        finally
        {
            TryDeleteTempFile(tempFilePath);
        }
    }

    private static void TryDeleteTempFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
