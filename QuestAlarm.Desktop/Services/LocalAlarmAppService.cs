using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.ValueObjects;

namespace QuestAlarm.Desktop.Services;

public sealed class LocalAlarmAppService : IAlarmAppService
{
    private readonly IAlarmRepository _alarmRepository;

    public LocalAlarmAppService(IAlarmRepository alarmRepository)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
    }

    public async Task<IReadOnlyList<Alarm>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        var alarms = await _alarmRepository.GetAllAsync(cancellationToken);
        return alarms
            .OrderBy(a => a.Schedule.IsRecurring ? 0 : 1)
            .ThenBy(a => a.Schedule.StartDate ?? DateOnly.MaxValue)
            .ThenBy(a => a.Schedule.Time)
            .ThenBy(a => a.Title)
            .ToList();
    }

    public async Task<Alarm?> ToggleEnabledAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return null;
        }

        if (alarm.IsEnabled)
        {
            alarm.Disable();
        }
        else
        {
            alarm.Enable();
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return alarm;
    }

    public async Task<(Alarm? Alarm, string? Error)> CreateAsync(AlarmFormModel model, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSchedule(model, out var schedule, out var error))
        {
            return (null, error);
        }

        Alarm alarm;
        try
        {
            alarm = new Alarm(
                Guid.NewGuid(),
                model.Title,
                schedule!,
                isEnabled: true,
                state: AlarmState.Scheduled,
                createdAtUtc: DateTime.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return (alarm, null);
    }

    public async Task<(Alarm? Alarm, string? Error)> UpdateAsync(
        Guid alarmId,
        AlarmFormModel model,
        CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmRepository.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return (null, "Alarm not found.");
        }

        if (model.IsRecurring != alarm.Schedule.IsRecurring)
        {
            return (null, "Changing alarm type is not supported in edit flow.");
        }

        if (!TryBuildSchedule(model, out var schedule, out var error))
        {
            return (null, error);
        }

        try
        {
            alarm.UpdateTitle(model.Title);
            alarm.UpdateSchedule(schedule!);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }

        if (alarm.IsEnabled)
        {
            alarm.Enable();
        }

        await _alarmRepository.SaveAsync(alarm, cancellationToken);
        return (alarm, null);
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

    private static bool TryBuildSchedule(AlarmFormModel model, out AlarmSchedule? schedule, out string? error)
    {
        schedule = null;
        error = null;

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            error = "Title is required.";
            return false;
        }

        if (!TimeOnly.TryParse(model.Time, out var time))
        {
            error = "Time must be valid (HH:mm).";
            return false;
        }

        if (model.IsRecurring)
        {
            var recurringDays = ParseRecurringDays(model.RecurringDaysCsv);
            if (recurringDays.Count == 0)
            {
                error = "Recurring days are required (for example: Mon,Wed,Fri).";
                return false;
            }

            schedule = new AlarmSchedule(
                time,
                isRecurring: true,
                recurringDays: recurringDays);
            return true;
        }

        if (string.IsNullOrWhiteSpace(model.StartDate) ||
            !DateOnly.TryParse(model.StartDate, out var startDate))
        {
            error = "Start date is required for one-time alarms (yyyy-MM-dd).";
            return false;
        }

        schedule = new AlarmSchedule(
            time,
            startDate: startDate,
            isRecurring: false);
        return true;
    }

    private static IReadOnlyCollection<DayOfWeek> ParseRecurringDays(string? rawDays)
    {
        if (string.IsNullOrWhiteSpace(rawDays))
        {
            return Array.Empty<DayOfWeek>();
        }

        var results = new List<DayOfWeek>();
        var parts = rawDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (TryParseDayOfWeek(part, out var day) && !results.Contains(day))
            {
                results.Add(day);
            }
        }

        return results;
    }

    private static bool TryParseDayOfWeek(string value, out DayOfWeek day)
    {
        day = default;
        if (int.TryParse(value, out var dayNumber))
        {
            day = dayNumber switch
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

        return Enum.TryParse(value, ignoreCase: true, out day);
    }
}
