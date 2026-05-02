namespace QuestAlarm.Infrastructure.ChallengeClient;

public sealed class ChallengeClientOptions
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = "--session-id {SessionId} --alarm-id {AlarmId} --callback-url {CallbackUrl} --challenge-token {ChallengeToken}";
    public string CallbackUrl { get; set; } = "http://localhost:5055";
    public string WorkingDirectory { get; set; } = string.Empty;
}
