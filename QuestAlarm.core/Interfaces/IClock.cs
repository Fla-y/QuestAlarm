namespace QuestAlarm.Core.Interfaces;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
}