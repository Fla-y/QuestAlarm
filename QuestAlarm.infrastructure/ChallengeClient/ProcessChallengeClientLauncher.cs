using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;
using System.Diagnostics;
using System.Text;

namespace QuestAlarm.Infrastructure.ChallengeClient;

public sealed class ProcessChallengeClientLauncher : IChallengeClientLauncher
{
    private readonly ChallengeClientOptions _options;

    public ProcessChallengeClientLauncher(ChallengeClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ChallengeClientLaunchResult> LaunchAsync(
        AlarmSession session,
        Alarm alarm,
        string? challengeConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(alarm);

        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = Environment.ExpandEnvironmentVariables(_options.ExecutablePath);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Task.FromResult(ChallengeClientLaunchResult.NotStarted("Challenge client executable path is not configured."));
        }

        if (!File.Exists(executablePath))
        {
            return Task.FromResult(ChallengeClientLaunchResult.NotStarted($"Challenge client executable was not found: {executablePath}"));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        foreach (var argument in BuildArguments(session, alarm, challengeConfigPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Environment.ExpandEnvironmentVariables(_options.WorkingDirectory);
        }

        try
        {
            var process = Process.Start(startInfo);

            return process is null
                ? Task.FromResult(ChallengeClientLaunchResult.NotStarted("Challenge client process could not be started."))
                : Task.FromResult(ChallengeClientLaunchResult.Started(process.Id));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(ChallengeClientLaunchResult.NotStarted(ex.Message));
        }
    }

    private IReadOnlyCollection<string> BuildArguments(AlarmSession session, Alarm alarm, string? challengeConfigPath)
    {
        var arguments = _options.ArgumentsTemplate
            .Replace("{SessionId}", session.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{AlarmId}", alarm.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{CallbackUrl}", _options.CallbackUrl, StringComparison.OrdinalIgnoreCase)
            .Replace("{ChallengeToken}", session.ChallengeToken, StringComparison.OrdinalIgnoreCase)
            .Replace("{ChallengeConfigPath}", challengeConfigPath ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        return SplitArguments(arguments);
    }

    private static IReadOnlyCollection<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrentArgument();
                continue;
            }

            current.Append(character);
        }

        AddCurrentArgument();

        return result;

        void AddCurrentArgument()
        {
            if (current.Length == 0)
            {
                return;
            }

            result.Add(current.ToString());
            current.Clear();
        }
    }
}
