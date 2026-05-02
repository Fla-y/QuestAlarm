using QuestAlarm.Core.Entities;

namespace QuestAlarm.ConsoleHost.Helpers;

public static class ConsoleInput
{
    public static IReadOnlyCollection<DayOfWeek> ParseRecurringDays(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<DayOfWeek>();
        }

        var result = new List<DayOfWeek>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var dayNumber))
            {
                continue;
            }

            var dayOfWeek = dayNumber switch
            {
                1 => DayOfWeek.Monday,
                2 => DayOfWeek.Tuesday,
                3 => DayOfWeek.Wednesday,
                4 => DayOfWeek.Thursday,
                5 => DayOfWeek.Friday,
                6 => DayOfWeek.Saturday,
                7 => DayOfWeek.Sunday,
                _ => (DayOfWeek?)null
            };

            if (dayOfWeek is not null && !result.Contains(dayOfWeek.Value))
            {
                result.Add(dayOfWeek.Value);
            }
        }

        return result;
    }

    public static Alarm? SelectAlarmFromList(IReadOnlyList<Alarm> alarms, string prompt)
    {
        Console.WriteLine(prompt);

        for (var index = 0; index < alarms.Count; index++)
        {
            var alarm = alarms[index];
            var typeLabel = alarm.Schedule.IsRecurring ? "Recurring" : "One-time";
            Console.WriteLine($"{index + 1}. {alarm.Title} | {typeLabel} | Enabled: {alarm.IsEnabled} | Time: {alarm.Schedule.Time}");
        }

        Console.Write("Choice: ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out var selectedIndex) ||
            selectedIndex < 1 ||
            selectedIndex > alarms.Count)
        {
            return null;
        }

        return alarms[selectedIndex - 1];
    }

    public static void WriteRecurringDaysLegend()
    {
        Console.WriteLine("Recurring days:");
        Console.WriteLine("1 = Monday");
        Console.WriteLine("2 = Tuesday");
        Console.WriteLine("3 = Wednesday");
        Console.WriteLine("4 = Thursday");
        Console.WriteLine("5 = Friday");
        Console.WriteLine("6 = Saturday");
        Console.WriteLine("7 = Sunday");
    }
}
