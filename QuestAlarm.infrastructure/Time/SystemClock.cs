using QuestAlarm.Core.Interfaces;

namespace QuestAlarm.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => DateTime.Now;
}