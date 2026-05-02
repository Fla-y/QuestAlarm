namespace QuestAlarm.Infrastructure.Persistence.Models;

public sealed class AlarmSessionFileModel
{
    public Guid Id { get; set; }
    public Guid AlarmId { get; set; }
    public DateTime TriggeredAtUtc { get; set; }
    public int State { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ChallengeToken { get; set; }
}
