namespace QuestAlarm.Infrastructure.Persistence;

public sealed class StoragePaths
{
    private const string DefaultRootTemplate = "%LOCALAPPDATA%\\QuestAlarm";

    public string RootDirectory { get; }
    public string AlarmsDirectory { get; }
    public string SessionsDirectory { get; }

    public StoragePaths(string? rootDirectory)
    {
        RootDirectory = ResolveRootDirectory(rootDirectory);
        AlarmsDirectory = Path.Combine(RootDirectory, "Alarms");
        SessionsDirectory = Path.Combine(RootDirectory, "Sessions");
    }

    private static string ResolveRootDirectory(string? rootDirectory)
    {
        var value = string.IsNullOrWhiteSpace(rootDirectory)
            ? DefaultRootTemplate
            : rootDirectory;

        return Environment.ExpandEnvironmentVariables(value);
    }
}
