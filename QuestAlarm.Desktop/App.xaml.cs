using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestAlarm.Application.Alarms;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Services;
using QuestAlarm.Desktop.Services;
using QuestAlarm.Infrastructure.ChallengeClient;
using QuestAlarm.Infrastructure.Persistence;
using QuestAlarm.Infrastructure.Time;
using Serilog;
using System.Windows;

namespace QuestAlarm.Desktop;

public partial class App : System.Windows.Application
{
    private readonly IConfiguration _configuration;
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        var storagePaths = new StoragePaths(configuration["Storage:RootDirectory"]);
        var challengeClientOptions = new ChallengeClientOptions
        {
            ExecutablePath = configuration["ChallengeClient:ExecutablePath"] ?? string.Empty,
            ArgumentsTemplate = configuration["ChallengeClient:ArgumentsTemplate"] ?? "--config \"{ChallengeConfigPath}\" --session-id {SessionId} --alarm-id {AlarmId} --callback-url {CallbackUrl} --challenge-token {ChallengeToken}",
            CallbackUrl = configuration["ChallengeClient:CallbackUrl"] ?? "http://localhost:5055",
            WorkingDirectory = configuration["ChallengeClient:WorkingDirectory"] ?? string.Empty
        };

        services.AddWpfBlazorWebView();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Log.Logger);
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
        services.AddSingleton<IAlarmManagementService, AlarmManagementService>();
        services.AddSingleton<IChallengeClientLauncher, ProcessChallengeClientLauncher>();
        services.AddSingleton<ChallengeConfigService>();
        services.AddSingleton<ChallengeLaunchService>();
        services.AddSingleton<IChallengeActivityService, ChallengeActivityService>();
        services.AddSingleton<IAlarmNotificationService, WpfAlarmNotificationService>();
        services.AddSingleton<DashboardDataService>();
        services.AddSingleton<SessionListService>();
        services.AddSingleton<DesktopSettingsService>();
        services.AddSingleton<DesktopRuntimeService>();
        services.AddSingleton<IAlarmAppService, LocalAlarmAppService>();

        _configuration = configuration;
        _serviceProvider = services.BuildServiceProvider();

        Resources.Add("Services", _serviceProvider);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Information("QuestAlarm Desktop started");

        if (!_configuration.GetValue("Runtime:AutoStart", defaultValue: true))
        {
            Log.Information("Desktop runtime auto-start is disabled");
            return;
        }

        try
        {
            await _serviceProvider.GetRequiredService<DesktopRuntimeService>().StartAsync();
            Log.Information("Desktop runtime auto-started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Desktop runtime auto-start failed");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _serviceProvider.GetRequiredService<DesktopRuntimeService>()
                .StopAsync()
                .GetAwaiter()
                .GetResult();

            _serviceProvider.Dispose();
            Log.Information("QuestAlarm Desktop stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "QuestAlarm Desktop shutdown failed");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
