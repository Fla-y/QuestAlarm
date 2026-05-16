using QuestAlarm.ConsoleHost.Helpers;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.ValueObjects;
using Serilog;

namespace QuestAlarm.ConsoleHost;

public sealed class AlarmConsoleActions
{
    private readonly IAlarmRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public AlarmConsoleActions(IAlarmRepository repository, IClock clock, ILogger logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ListAlarmsAsync()
    {
        var alarms = (await _repository.GetAllAsync())
            .OrderBy(a => a.Title)
            .ToList();

        if (alarms.Count == 0)
        {
            Console.WriteLine("No alarms found.");
            return;
        }

        Console.WriteLine("Saved alarms:");
        foreach (var alarm in alarms)
        {
            var typeLabel = alarm.Schedule.IsRecurring ? "Recurring" : "One-time";
            var dateLabel = alarm.Schedule.StartDate?.ToString() ?? "-";
            var recurringDaysLabel = alarm.Schedule.IsRecurring
                ? string.Join(", ", alarm.Schedule.RecurringDays)
                : "-";

            Console.WriteLine($"Id:        {alarm.Id}");
            Console.WriteLine($"Title:     {alarm.Title}");
            Console.WriteLine($"Type:      {typeLabel}");
            Console.WriteLine($"Time:      {alarm.Schedule.Time}");
            Console.WriteLine($"Date:      {dateLabel}");
            Console.WriteLine($"Days:      {recurringDaysLabel}");
            Console.WriteLine($"Challenge: {alarm.ChallengeType} / {alarm.ChallengeDifficulty}");
            Console.WriteLine($"Enabled:   {alarm.IsEnabled}");
            Console.WriteLine($"State:     {alarm.State}");
            Console.WriteLine(new string('-', 50));
        }
    }

    public async Task CreateOneTimeAlarmAsync()
    {
        Console.Write("Title: ");
        var title = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            Console.WriteLine("Title cannot be empty.");
            return;
        }

        Console.Write("Date (yyyy-MM-dd): ");
        var dateInput = Console.ReadLine();

        if (!DateOnly.TryParse(dateInput, out var startDate))
        {
            Console.WriteLine("Invalid date.");
            return;
        }

        Console.Write("Time (HH:mm): ");
        var timeInput = Console.ReadLine();

        if (!TimeOnly.TryParse(timeInput, out var time))
        {
            Console.WriteLine("Invalid time.");
            return;
        }

        var alarm = new Alarm(
            Guid.NewGuid(),
            title,
            new AlarmSchedule(
                time,
                startDate: startDate,
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: _clock.UtcNow);

        await _repository.SaveAsync(alarm);

        _logger.Information("Created one-time alarm {AlarmId} titled {AlarmTitle} for {AlarmDate} at {AlarmTime}",
            alarm.Id,
            alarm.Title,
            startDate,
            time);

        Console.WriteLine("One-time alarm created successfully.");
        Console.WriteLine($"Alarm id: {alarm.Id}");
    }

    public async Task CreateRecurringAlarmAsync()
    {
        Console.Write("Title: ");
        var title = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            Console.WriteLine("Title cannot be empty.");
            return;
        }

        Console.Write("Time (HH:mm): ");
        var timeInput = Console.ReadLine();

        if (!TimeOnly.TryParse(timeInput, out var time))
        {
            Console.WriteLine("Invalid time.");
            return;
        }

        ConsoleInput.WriteRecurringDaysLegend();
        Console.Write("Choose days (example: 1,2,3,4,5): ");

        var daysInput = Console.ReadLine();
        var recurringDays = ConsoleInput.ParseRecurringDays(daysInput);

        if (recurringDays.Count == 0)
        {
            Console.WriteLine("You must select at least one valid day.");
            return;
        }

        var alarm = new Alarm(
            Guid.NewGuid(),
            title,
            new AlarmSchedule(
                time,
                isRecurring: true,
                recurringDays: recurringDays),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: _clock.UtcNow);

        await _repository.SaveAsync(alarm);

        _logger.Information("Created recurring alarm {AlarmId} titled {AlarmTitle} at {AlarmTime} on {RecurringDays}",
            alarm.Id,
            alarm.Title,
            time,
            recurringDays);

        Console.WriteLine("Recurring alarm created successfully.");
        Console.WriteLine($"Alarm id: {alarm.Id}");
    }

    public async Task CreateTestAlarmDueSoonAsync()
    {
        var dueAtLocal = DateTime.Now.AddMinutes(1);
        dueAtLocal = new DateTime(
            dueAtLocal.Year,
            dueAtLocal.Month,
            dueAtLocal.Day,
            dueAtLocal.Hour,
            dueAtLocal.Minute,
            0,
            dueAtLocal.Kind);

        var alarm = new Alarm(
            Guid.NewGuid(),
            $"Test alarm {dueAtLocal:HH:mm}",
            new AlarmSchedule(
                TimeOnly.FromDateTime(dueAtLocal),
                startDate: DateOnly.FromDateTime(dueAtLocal),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: _clock.UtcNow);

        await _repository.SaveAsync(alarm);

        _logger.Information(
            "Created test alarm {AlarmId} titled {AlarmTitle} due at {DueAtLocal}",
            alarm.Id,
            alarm.Title,
            dueAtLocal);

        Console.WriteLine("Test alarm created successfully.");
        Console.WriteLine($"Alarm id: {alarm.Id}");
        Console.WriteLine($"Due at:   {dueAtLocal:yyyy-MM-dd HH:mm:ss}");
    }

    public async Task ToggleAlarmEnabledAsync()
    {
        var alarms = await GetOrderedAlarmsAsync();

        if (alarms.Count == 0)
        {
            Console.WriteLine("No alarms found.");
            return;
        }

        var selectedAlarm = ConsoleInput.SelectAlarmFromList(alarms, "Select an alarm to enable/disable:");

        if (selectedAlarm is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        if (selectedAlarm.IsEnabled)
        {
            selectedAlarm.Disable();
            _logger.Information("Disabled alarm {AlarmId} titled {AlarmTitle}", selectedAlarm.Id, selectedAlarm.Title);
            Console.WriteLine($"Alarm '{selectedAlarm.Title}' disabled.");
        }
        else
        {
            selectedAlarm.Enable();
            _logger.Information("Enabled alarm {AlarmId} titled {AlarmTitle}", selectedAlarm.Id, selectedAlarm.Title);
            Console.WriteLine($"Alarm '{selectedAlarm.Title}' enabled.");
        }

        await _repository.SaveAsync(selectedAlarm);
    }

    public async Task DeleteAlarmAsync()
    {
        var alarms = await GetOrderedAlarmsAsync();

        if (alarms.Count == 0)
        {
            Console.WriteLine("No alarms found.");
            return;
        }

        var selectedAlarm = ConsoleInput.SelectAlarmFromList(alarms, "Select an alarm to delete:");

        if (selectedAlarm is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        Console.Write($"Are you sure you want to delete '{selectedAlarm.Title}'? (y/n): ");
        var confirmation = Console.ReadLine()?.Trim();

        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Deletion cancelled.");
            return;
        }

        await _repository.DeleteAsync(selectedAlarm.Id);

        _logger.Information("Deleted alarm {AlarmId} titled {AlarmTitle}", selectedAlarm.Id, selectedAlarm.Title);

        Console.WriteLine($"Alarm '{selectedAlarm.Title}' deleted.");
    }

    public async Task EditAlarmAsync()
    {
        var alarms = await GetOrderedAlarmsAsync();

        if (alarms.Count == 0)
        {
            Console.WriteLine("No alarms found.");
            return;
        }

        var selectedAlarm = ConsoleInput.SelectAlarmFromList(alarms, "Select an alarm to edit:");

        if (selectedAlarm is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        if (selectedAlarm.Schedule.IsRecurring)
        {
            await EditRecurringAlarmAsync(selectedAlarm);
        }
        else
        {
            await EditOneTimeAlarmAsync(selectedAlarm);
        }
    }

    private async Task<List<Alarm>> GetOrderedAlarmsAsync()
    {
        return (await _repository.GetAllAsync())
            .OrderBy(a => a.Title)
            .ToList();
    }

    private async Task EditOneTimeAlarmAsync(Alarm alarm)
    {
        Console.WriteLine($"Editing one-time alarm: {alarm.Title}");
        Console.WriteLine("Leave a field empty to keep the current value.");
        Console.WriteLine();

        Console.Write($"Title ({alarm.Title}): ");
        var titleInput = Console.ReadLine()?.Trim();

        if (!string.IsNullOrWhiteSpace(titleInput))
        {
            alarm.UpdateTitle(titleInput);
        }

        var currentDate = alarm.Schedule.StartDate?.ToString("yyyy-MM-dd") ?? string.Empty;
        Console.Write($"Date ({currentDate}): ");
        var dateInput = Console.ReadLine()?.Trim();

        DateOnly startDate;
        if (string.IsNullOrWhiteSpace(dateInput))
        {
            if (alarm.Schedule.StartDate is null)
            {
                Console.WriteLine("This one-time alarm has no valid date.");
                return;
            }

            startDate = alarm.Schedule.StartDate.Value;
        }
        else if (!DateOnly.TryParse(dateInput, out startDate))
        {
            Console.WriteLine("Invalid date.");
            return;
        }

        Console.Write($"Time ({alarm.Schedule.Time:HH\\:mm}): ");
        var timeInput = Console.ReadLine()?.Trim();

        TimeOnly time;
        if (string.IsNullOrWhiteSpace(timeInput))
        {
            time = alarm.Schedule.Time;
        }
        else if (!TimeOnly.TryParse(timeInput, out time))
        {
            Console.WriteLine("Invalid time.");
            return;
        }

        alarm.UpdateSchedule(new AlarmSchedule(
            time,
            startDate: startDate,
            isRecurring: false));

        MarkEditedAlarmScheduledIfEnabled(alarm);

        await _repository.SaveAsync(alarm);

        _logger.Information("Updated one-time alarm {AlarmId} titled {AlarmTitle}", alarm.Id, alarm.Title);

        Console.WriteLine("One-time alarm updated successfully.");
    }

    private async Task EditRecurringAlarmAsync(Alarm alarm)
    {
        Console.WriteLine($"Editing recurring alarm: {alarm.Title}");
        Console.WriteLine("Leave a field empty to keep the current value.");
        Console.WriteLine();

        Console.Write($"Title ({alarm.Title}): ");
        var titleInput = Console.ReadLine()?.Trim();

        if (!string.IsNullOrWhiteSpace(titleInput))
        {
            alarm.UpdateTitle(titleInput);
        }

        Console.Write($"Time ({alarm.Schedule.Time:HH\\:mm}): ");
        var timeInput = Console.ReadLine()?.Trim();

        TimeOnly time;
        if (string.IsNullOrWhiteSpace(timeInput))
        {
            time = alarm.Schedule.Time;
        }
        else if (!TimeOnly.TryParse(timeInput, out time))
        {
            Console.WriteLine("Invalid time.");
            return;
        }

        ConsoleInput.WriteRecurringDaysLegend();
        Console.WriteLine($"Current days: {string.Join(", ", alarm.Schedule.RecurringDays)}");
        Console.Write("Choose days (leave empty to keep current): ");

        var daysInput = Console.ReadLine()?.Trim();

        IReadOnlyCollection<DayOfWeek> recurringDays;
        if (string.IsNullOrWhiteSpace(daysInput))
        {
            recurringDays = alarm.Schedule.RecurringDays;
        }
        else
        {
            recurringDays = ConsoleInput.ParseRecurringDays(daysInput);

            if (recurringDays.Count == 0)
            {
                Console.WriteLine("You must select at least one valid day.");
                return;
            }
        }

        alarm.UpdateSchedule(new AlarmSchedule(
            time,
            isRecurring: true,
            recurringDays: recurringDays));

        MarkEditedAlarmScheduledIfEnabled(alarm);

        await _repository.SaveAsync(alarm);

        _logger.Information("Updated recurring alarm {AlarmId} titled {AlarmTitle}", alarm.Id, alarm.Title);

        Console.WriteLine("Recurring alarm updated successfully.");
    }

    private static void MarkEditedAlarmScheduledIfEnabled(Alarm alarm)
    {
        if (alarm.IsEnabled)
        {
            alarm.Enable();
        }
    }
}
