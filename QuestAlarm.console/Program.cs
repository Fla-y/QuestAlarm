using QuestAlarm.ConsoleHost;
using QuestAlarm.Core.Services;
using QuestAlarm.Infrastructure.ChallengeClient;
using QuestAlarm.Infrastructure.Persistence;
using QuestAlarm.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(configuration)
        .CreateLogger();

    var storagePaths = new StoragePaths(configuration["Storage:RootDirectory"]);
    var dataDirectory = storagePaths.AlarmsDirectory;
    var sessionsDirectory = storagePaths.SessionsDirectory;

    Log.Information("QuestAlarm console host starting");
    Log.Information("Alarm data directory: {DataDirectory}", dataDirectory);
    Log.Information("Session data directory: {SessionsDirectory}", sessionsDirectory);

    var clock = new SystemClock();
    var repository = new JsonAlarmRepository(dataDirectory);
    var sessionRepository = new JsonAlarmSessionRepository(sessionsDirectory);
    var challengeClientOptions = configuration
        .GetSection("ChallengeClient")
        .Get<ChallengeClientOptions>() ?? new ChallengeClientOptions();
    var developmentOptions = configuration
        .GetSection("Development")
        .Get<DevelopmentOptions>() ?? new DevelopmentOptions();

    var occurrenceCalculator = new AlarmOccurrenceCalculator();
    var dueEvaluator = new AlarmDueEvaluator(occurrenceCalculator);
    var scheduler = new AlarmScheduler(dueEvaluator);
    var triggerService = new AlarmTriggerService();
    var missedService = new AlarmMissedService();
    var sessionService = new AlarmSessionService();
    var challengeClientLauncher = new ProcessChallengeClientLauncher(challengeClientOptions);

    var alarmActions = new AlarmConsoleActions(repository, clock, Log.Logger);
    var sessionActions = new SessionConsoleActions(repository, sessionRepository, sessionService, clock, Log.Logger);
    var schedulerRunner = new SchedulerConsoleRunner(
        repository,
        sessionRepository,
        clock,
        dueEvaluator,
        scheduler,
        triggerService,
        missedService,
        challengeClientLauncher,
        Log.Logger);

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("=== QuestAlarm ===");
        Console.WriteLine("1. List alarms");
        Console.WriteLine("2. Create one-time alarm");
        Console.WriteLine("3. Create recurring alarm");
        Console.WriteLine("4. Enable/disable alarm");
        Console.WriteLine("5. Delete alarm");
        Console.WriteLine("6. Start scheduler");
        Console.WriteLine("7. Edit alarm");
        Console.WriteLine("8. List sessions");
        Console.WriteLine("9. Mark session challenge running");
        Console.WriteLine("10. Complete session");
        Console.WriteLine("11. Fail session");
        if (developmentOptions.ShowTestAlarmMenuOption)
        {
            Console.WriteLine("12. Create test alarm due soon");
        }
        Console.WriteLine("0. Exit");
        Console.Write("Choose an option: ");

        var choice = Console.ReadLine();

        if (choice is null)
        {
            Log.Information("Input stream closed; QuestAlarm console host stopping");
            return;
        }

        choice = choice.Trim().Trim('\uFEFF');

        Console.WriteLine();

        switch (choice)
        {
            case "1":
                await alarmActions.ListAlarmsAsync();
                break;

            case "2":
                await alarmActions.CreateOneTimeAlarmAsync();
                break;

            case "3":
                await alarmActions.CreateRecurringAlarmAsync();
                break;

            case "4":
                await alarmActions.ToggleAlarmEnabledAsync();
                break;

            case "5":
                await alarmActions.DeleteAlarmAsync();
                break;

            case "6":
                await schedulerRunner.RunAsync();
                break;

            case "7":
                await alarmActions.EditAlarmAsync();
                break;

            case "8":
                await sessionActions.ListSessionsAsync();
                break;

            case "9":
                await sessionActions.StartChallengeAsync();
                break;

            case "10":
                await sessionActions.CompleteSessionAsync();
                break;

            case "11":
                await sessionActions.FailSessionAsync();
                break;

            case "12" when developmentOptions.ShowTestAlarmMenuOption:
                await alarmActions.CreateTestAlarmDueSoonAsync();
                break;

            case "0":
                Log.Information("QuestAlarm console host stopped by user");
                return;

            default:
                Console.WriteLine("Invalid option.");
                Log.Warning("Invalid menu option selected: {Choice}", choice);
                break;
        }
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "QuestAlarm console host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
