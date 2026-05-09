using QuestAlarm.Core.Entities;

namespace QuestAlarm.Application.Alarms;

public enum AlarmMutationErrorKind
{
    Validation,
    NotFound
}

public sealed record AlarmMutationResult(
    Alarm? Alarm,
    string? Error,
    AlarmMutationErrorKind? ErrorKind)
{
    public bool Succeeded => Alarm is not null && Error is null;

    public static AlarmMutationResult Success(Alarm alarm)
    {
        return new AlarmMutationResult(alarm, null, null);
    }

    public static AlarmMutationResult Failure(
        string error,
        AlarmMutationErrorKind errorKind = AlarmMutationErrorKind.Validation)
    {
        return new AlarmMutationResult(null, error, errorKind);
    }
}
