using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Application.Alarms;

public sealed class AlarmManagementService : IAlarmManagementService
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IClock _clock;

    public AlarmManagementService(IAlarmRepository alarmRepository, IClock clock)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<Alarm>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        var alarms = await _alarmRepository.GetAllAsync(cancellationToken);
        return alarms
            .OrderBy(alarm => alarm.Schedule.IsRecurring ? 0 : 1)
            .ThenBy(alarm => alarm.Schedule.StartDate ?? DateOnly.MaxValue)
            .ThenBy(alarm => alarm.Schedule.Time)
            .ThenBy(alarm => alarm.Title)
            .ToList();
    }

    public Task<Alarm?> GetByIdAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        return _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
    }

    public async Task<AlarmMutationResult> CreateAsync(
        CreateAlarmCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildScheduleForCreate(command, out var schedule, out var validationError))
        {
            return AlarmMutationResult.Failure(validationError!);
        }

        Alarm alarm;
        try
        {
            alarm = new Alarm(
                Guid.NewGuid(),
                command.Title,
                schedule!,
                isEnabled: true,
                state: AlarmState.Scheduled,
                createdAtUtc: _clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return AlarmMutationResult.Failure(ex.Message);
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return AlarmMutationResult.Success(alarm);
    }

    public async Task<AlarmMutationResult> UpdateAsync(
        Guid alarmId,
        UpdateAlarmCommand command,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return AlarmMutationResult.Failure("Alarm not found.", AlarmMutationErrorKind.NotFound);
        }

        if (!TryApplyAlarmUpdate(alarm, command, out var validationError))
        {
            return AlarmMutationResult.Failure(validationError!);
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return AlarmMutationResult.Success(alarm);
    }

    public async Task<Alarm?> SetEnabledAsync(
        Guid alarmId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return null;
        }

        if (isEnabled)
        {
            alarm.Enable();
        }
        else
        {
            alarm.Disable();
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return alarm;
    }

    public async Task<bool> DeleteAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return false;
        }

        await _alarmRepository.DeleteAsync(alarmId, cancellationToken);
        return true;
    }

    private static bool TryBuildScheduleForCreate(
        CreateAlarmCommand command,
        out AlarmSchedule? schedule,
        out string? validationError)
    {
        schedule = null;
        validationError = null;

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            validationError = "Title is required.";
            return false;
        }

        if (!TimeOnly.TryParse(command.Time, out var time))
        {
            validationError = "Time must be a valid value in HH:mm format.";
            return false;
        }

        if (command.IsRecurring)
        {
            var recurringDays = ParseRecurringDays(command.RecurringDays);
            if (recurringDays.Count == 0)
            {
                validationError = "Recurring alarms require at least one valid recurring day.";
                return false;
            }

            schedule = new AlarmSchedule(
                time,
                isRecurring: true,
                recurringDays: recurringDays);
            return true;
        }

        if (string.IsNullOrWhiteSpace(command.StartDate) ||
            !DateOnly.TryParse(command.StartDate, out var startDate))
        {
            validationError = "One-time alarms require a valid start date in yyyy-MM-dd format.";
            return false;
        }

        schedule = new AlarmSchedule(
            time,
            startDate: startDate,
            isRecurring: false);
        return true;
    }

    private static bool TryApplyAlarmUpdate(
        Alarm alarm,
        UpdateAlarmCommand command,
        out string? validationError)
    {
        validationError = null;

        if (command.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(command.Title))
            {
                validationError = "Title cannot be empty.";
                return false;
            }

            try
            {
                alarm.UpdateTitle(command.Title);
            }
            catch (ArgumentException ex)
            {
                validationError = ex.Message;
                return false;
            }
        }

        var hasScheduleChange =
            command.Time is not null ||
            command.StartDate is not null ||
            command.RecurringDays is not null;

        if (!hasScheduleChange)
        {
            return true;
        }

        if (alarm.Schedule.IsRecurring)
        {
            if (command.StartDate is not null)
            {
                validationError = "Recurring alarms do not support startDate updates.";
                return false;
            }

            var time = alarm.Schedule.Time;
            if (command.Time is not null && !TimeOnly.TryParse(command.Time, out time))
            {
                validationError = "Time must be a valid value in HH:mm format.";
                return false;
            }

            var recurringDays = alarm.Schedule.RecurringDays;
            if (command.RecurringDays is not null)
            {
                recurringDays = ParseRecurringDays(command.RecurringDays);
                if (recurringDays.Count == 0)
                {
                    validationError = "Recurring alarms require at least one valid recurring day.";
                    return false;
                }
            }

            alarm.UpdateSchedule(new AlarmSchedule(
                time,
                isRecurring: true,
                recurringDays: recurringDays));
        }
        else
        {
            if (command.RecurringDays is not null)
            {
                validationError = "One-time alarms do not support recurringDays updates.";
                return false;
            }

            var time = alarm.Schedule.Time;
            if (command.Time is not null && !TimeOnly.TryParse(command.Time, out time))
            {
                validationError = "Time must be a valid value in HH:mm format.";
                return false;
            }

            var startDate = alarm.Schedule.StartDate;
            if (command.StartDate is not null)
            {
                if (!DateOnly.TryParse(command.StartDate, out var parsedDate))
                {
                    validationError = "StartDate must be a valid value in yyyy-MM-dd format.";
                    return false;
                }

                startDate = parsedDate;
            }

            if (startDate is null)
            {
                validationError = "One-time alarms require a start date.";
                return false;
            }

            alarm.UpdateSchedule(new AlarmSchedule(
                time,
                startDate: startDate.Value,
                isRecurring: false));
        }

        if (alarm.IsEnabled)
        {
            alarm.Enable();
        }

        return true;
    }

    private static IReadOnlyCollection<DayOfWeek> ParseRecurringDays(IReadOnlyCollection<string>? rawDays)
    {
        if (rawDays is null || rawDays.Count == 0)
        {
            return Array.Empty<DayOfWeek>();
        }

        var result = new List<DayOfWeek>();

        foreach (var rawDay in rawDays)
        {
            if (TryParseDayOfWeek(rawDay, out var dayOfWeek) && !result.Contains(dayOfWeek))
            {
                result.Add(dayOfWeek);
            }
        }

        return result;
    }

    private static bool TryParseDayOfWeek(string? value, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();

        if (int.TryParse(normalized, out var dayNumber))
        {
            dayOfWeek = dayNumber switch
            {
                1 => DayOfWeek.Monday,
                2 => DayOfWeek.Tuesday,
                3 => DayOfWeek.Wednesday,
                4 => DayOfWeek.Thursday,
                5 => DayOfWeek.Friday,
                6 => DayOfWeek.Saturday,
                7 => DayOfWeek.Sunday,
                _ => default
            };

            return dayNumber is >= 1 and <= 7;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out dayOfWeek);
    }
}
