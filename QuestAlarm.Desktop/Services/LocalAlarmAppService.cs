using QuestAlarm.Application.Alarms;
using Serilog;

namespace QuestAlarm.Desktop.Services;

public sealed class LocalAlarmAppService : IAlarmAppService
{
    private readonly IAlarmManagementService _alarmManagementService;
    private readonly ILogger _logger;

    public LocalAlarmAppService(
        IAlarmManagementService alarmManagementService,
        ILogger logger)
    {
        _alarmManagementService = alarmManagementService ?? throw new ArgumentNullException(nameof(alarmManagementService));
        _logger = logger.ForContext<LocalAlarmAppService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<Core.Entities.Alarm>> GetAlarmsAsync(CancellationToken cancellationToken = default)
    {
        return _alarmManagementService.GetAlarmsAsync(cancellationToken);
    }

    public async Task<Core.Entities.Alarm?> ToggleEnabledAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmManagementService.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return null;
        }

        var updatedAlarm = await _alarmManagementService.SetEnabledAsync(
            alarmId,
            isEnabled: !alarm.IsEnabled,
            cancellationToken);

        return updatedAlarm;
    }

    public async Task<(Core.Entities.Alarm? Alarm, string? Error)> CreateAsync(
        AlarmFormModel model,
        CancellationToken cancellationToken = default)
    {
        var result = await _alarmManagementService.CreateAsync(
            new CreateAlarmCommand(
                model.Title,
                model.Time,
                model.StartDate,
                model.IsRecurring,
                ParseRecurringDayTokens(model.RecurringDaysCsv),
                model.ChallengeType,
                model.ChallengeDifficulty),
            cancellationToken);

        if (!result.Succeeded)
        {
            return (null, result.Error);
        }

        _logger.Information(
            "Alarm created {AlarmId} {AlarmTitle} {IsRecurring} {ScheduledTime} {StartDate}",
            result.Alarm!.Id,
            result.Alarm.Title,
            result.Alarm.Schedule.IsRecurring,
            result.Alarm.Schedule.Time,
            result.Alarm.Schedule.StartDate);

        return (result.Alarm, null);
    }

    public async Task<(Core.Entities.Alarm? Alarm, string? Error)> UpdateAsync(
        Guid alarmId,
        AlarmFormModel model,
        CancellationToken cancellationToken = default)
    {
        var result = await _alarmManagementService.UpdateAsync(
            alarmId,
            new UpdateAlarmCommand(
                model.Title,
                model.Time,
                model.IsRecurring ? null : model.StartDate,
                model.IsRecurring ? ParseRecurringDayTokens(model.RecurringDaysCsv) : null,
                model.ChallengeType,
                model.ChallengeDifficulty),
            cancellationToken);

        if (!result.Succeeded)
        {
            return (null, result.Error);
        }

        _logger.Information(
            "Alarm updated {AlarmId} {AlarmTitle} {IsRecurring} {ScheduledTime} {StartDate}",
            result.Alarm!.Id,
            result.Alarm.Title,
            result.Alarm.Schedule.IsRecurring,
            result.Alarm.Schedule.Time,
            result.Alarm.Schedule.StartDate);

        return (result.Alarm, null);
    }

    public async Task<bool> DeleteAsync(Guid alarmId, CancellationToken cancellationToken = default)
    {
        var alarm = await _alarmManagementService.GetByIdAsync(alarmId, cancellationToken);
        if (alarm is null)
        {
            return false;
        }

        var wasDeleted = await _alarmManagementService.DeleteAsync(alarmId, cancellationToken);
        if (!wasDeleted)
        {
            return false;
        }

        _logger.Information(
            "Alarm deleted {AlarmId} {AlarmTitle}",
            alarm.Id,
            alarm.Title);

        return true;
    }

    private static IReadOnlyCollection<string>? ParseRecurringDayTokens(string? recurringDaysCsv)
    {
        if (string.IsNullOrWhiteSpace(recurringDaysCsv))
        {
            return null;
        }

        return recurringDaysCsv.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
