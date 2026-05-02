using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Services;
using QuestAlarm.Desktop.Services;
using QuestAlarm.Infrastructure.ChallengeClient;
using QuestAlarm.Infrastructure.Persistence;
using QuestAlarm.Infrastructure.Time;
using System.Windows;

namespace QuestAlarm.Desktop;

public partial class App : Application
{
    public App()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var storagePaths = new StoragePaths(configuration["Storage:RootDirectory"]);
        var challengeClientOptions = new ChallengeClientOptions
        {
            ExecutablePath = configuration["ChallengeClient:ExecutablePath"] ?? string.Empty,
            ArgumentsTemplate = configuration["ChallengeClient:ArgumentsTemplate"] ?? "--session-id {SessionId} --alarm-id {AlarmId} --callback-url {CallbackUrl} --challenge-token {ChallengeToken}",
            CallbackUrl = configuration["ChallengeClient:CallbackUrl"] ?? "http://localhost:5055",
            WorkingDirectory = configuration["ChallengeClient:WorkingDirectory"] ?? string.Empty
        };

        services.AddWpfBlazorWebView();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(storagePaths);
        services.AddSingleton(challengeClientOptions);
        services.AddSingleton<IAlarmRepository>(_ => new JsonAlarmRepository(storagePaths.AlarmsDirectory));
        services.AddSingleton<IAlarmSessionRepository>(_ => new JsonAlarmSessionRepository(storagePaths.SessionsDirectory));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAlarmOccurrenceCalculator, AlarmOccurrenceCalculator>();
        services.AddSingleton<IAlarmDueEvaluator, AlarmDueEvaluator>();
        services.AddSingleton<IAlarmScheduler, AlarmScheduler>();
        services.AddSingleton<IAlarmTriggerService, AlarmTriggerService>();
        services.AddSingleton<IAlarmMissedService, AlarmMissedService>();
        services.AddSingleton<IAlarmSessionService, AlarmSessionService>();
        services.AddSingleton<IChallengeClientLauncher, ProcessChallengeClientLauncher>();
        services.AddSingleton<DashboardDataService>();
        services.AddSingleton<SessionListService>();
        services.AddSingleton<DesktopSettingsService>();
        services.AddSingleton<DesktopRuntimeService>();
        services.AddSingleton<IAlarmAppService, LocalAlarmAppService>();

        Resources.Add("Services", services.BuildServiceProvider());
    }
}
