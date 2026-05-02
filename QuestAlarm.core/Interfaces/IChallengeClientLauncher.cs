using QuestAlarm.Core.Entities;

namespace QuestAlarm.Core.Interfaces;

public interface IChallengeClientLauncher
{
    Task<ChallengeClientLaunchResult> LaunchAsync(
        AlarmSession session,
        Alarm alarm,
        CancellationToken cancellationToken = default);
}
