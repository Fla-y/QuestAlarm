namespace QuestAlarm.Infrastructure.Persistence.Models;

public sealed class AlarmFileModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Hour { get; set; }
    public int Minute { get; set; }
    public DateOnly? StartDate { get; set; }
    public bool IsRecurring { get; set; }
    public List<DayOfWeek> RecurringDays { get; set; } = [];
    public string? ChallengeType { get; set; }
    public string? ChallengeDifficulty { get; set; }
    public bool IsEnabled { get; set; }
    public int State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
