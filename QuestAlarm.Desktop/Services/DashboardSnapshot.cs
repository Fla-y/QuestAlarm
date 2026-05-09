using QuestAlarm.Core.Entities;

namespace QuestAlarm.Desktop.Services;

public sealed record DashboardSnapshot(
    string StorageRoot,
    int AlarmCount,
    int EnabledAlarmCount,
    int SessionCount,
    Alarm? NextAlarm,
    DateTime? NextAlarmOccurrenceLocal,
    IReadOnlyList<Alarm> RecentAlarms,
    IReadOnlyList<AlarmSession> RecentSessions);
