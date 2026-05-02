namespace QuestAlarm.Core.Entities;

public sealed class ChallengeClientLaunchResult
{
    public bool WasStarted { get; }
    public int? ProcessId { get; }
    public string? ErrorMessage { get; }

    private ChallengeClientLaunchResult(bool wasStarted, int? processId, string? errorMessage)
    {
        WasStarted = wasStarted;
        ProcessId = processId;
        ErrorMessage = errorMessage;
    }

    public static ChallengeClientLaunchResult Started(int processId)
    {
        return new ChallengeClientLaunchResult(true, processId, null);
    }

    public static ChallengeClientLaunchResult NotStarted(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message cannot be empty.", nameof(errorMessage));
        }

        return new ChallengeClientLaunchResult(false, null, errorMessage);
    }
}
