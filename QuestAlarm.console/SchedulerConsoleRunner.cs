using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using Serilog;

namespace QuestAlarm.ConsoleHost;

public sealed class SchedulerConsoleRunner
{
    private readonly IAlarmRepository _repository;
    private readonly IAlarmSessionRepository _sessionRepository;
    private readonly IClock _clock;
    private readonly IAlarmDueEvaluator _dueEvaluator;
    private readonly IAlarmScheduler _scheduler;
    private readonly IAlarmTriggerService _triggerService;
    private readonly IAlarmMissedService _missedService;
    private readonly IChallengeClientLauncher _challengeClientLauncher;
    private readonly ILogger _logger;

    public SchedulerConsoleRunner(
        IAlarmRepository repository,
        IAlarmSessionRepository sessionRepository,
        IClock clock,
        IAlarmDueEvaluator dueEvaluator,
        IAlarmScheduler scheduler,
        IAlarmTriggerService triggerService,
        IAlarmMissedService missedService,
        IChallengeClientLauncher challengeClientLauncher,
        ILogger logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dueEvaluator = dueEvaluator ?? throw new ArgumentNullException(nameof(dueEvaluator));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _missedService = missedService ?? throw new ArgumentNullException(nameof(missedService));
        _challengeClientLauncher = challengeClientLauncher ?? throw new ArgumentNullException(nameof(challengeClientLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync()
    {
        _logger.Information("Scheduler started");

        Console.WriteLine("Scheduler started.");
        Console.WriteLine("Press Q to stop and return to the menu.");
        Console.WriteLine();

        while (true)
        {
            var alarms = (await _repository.GetAllAsync())
                .Where(a => a.IsEnabled)
                .ToList();

            var now = _clock.LocalNow;
            var dueTriggers = _scheduler.GetDueAlarms(alarms, now);

            foreach (var trigger in dueTriggers)
            {
                var session = _triggerService.Trigger(trigger, _clock.UtcNow);

                await _sessionRepository.SaveAsync(session);
                await _repository.SaveAsync(trigger.Alarm);
                var launchResult = await _challengeClientLauncher.LaunchAsync(session, trigger.Alarm);

                _logger.Warning(
                    "Alarm {AlarmId} titled {AlarmTitle} triggered for occurrence {OccurrenceLocalDateTime}; session {SessionId} saved with state {SessionState}; enabled={IsEnabled}",
                    trigger.Alarm.Id,
                    trigger.Alarm.Title,
                    trigger.Occurrence.OccurrenceLocalDateTime,
                    session.Id,
                    session.State,
                    trigger.Alarm.IsEnabled);

                if (launchResult.WasStarted)
                {
                    _logger.Information(
                        "Challenge client started for session {SessionId}; process id {ProcessId}",
                        session.Id,
                        launchResult.ProcessId);
                }
                else
                {
                    _logger.Warning(
                        "Challenge client was not started for session {SessionId}: {ErrorMessage}",
                        session.Id,
                        launchResult.ErrorMessage);
                }

                Console.WriteLine($"[{now:HH:mm:ss}] TRIGGERED: {trigger.Alarm.Title}");
                Console.WriteLine($"  Scheduled for: {trigger.Occurrence.OccurrenceLocalDateTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Session id: {session.Id}");
                Console.WriteLine($"  Enabled: {trigger.Alarm.IsEnabled}");
                Console.WriteLine(launchResult.WasStarted
                    ? $"  Challenge client process id: {launchResult.ProcessId}"
                    : $"  Challenge client: {launchResult.ErrorMessage}");

                var resolvedSession = await WaitForSessionResolutionAsync(session.Id);

                if (resolvedSession is not null)
                {
                    _logger.Information(
                        "Session {SessionId} resolved as {SessionState}",
                        resolvedSession.Id,
                        resolvedSession.State);

                    Console.WriteLine($"  Challenge result: {resolvedSession.State}");
                }

                Console.WriteLine();
            }

            foreach (var alarm in alarms.Where(a => a.IsEnabled && !a.Schedule.IsRecurring))
            {
                var evaluation = _dueEvaluator.Evaluate(alarm, now);

                if (evaluation.Status != AlarmDueStatus.Missed)
                {
                    continue;
                }

                _missedService.MarkMissed(alarm);

                await _repository.SaveAsync(alarm);

                _logger.Warning(
                    "One-time alarm {AlarmId} titled {AlarmTitle} marked missed and disabled",
                    alarm.Id,
                    alarm.Title);

                Console.WriteLine($"[{now:HH:mm:ss}] MISSED: {alarm.Title}");
                Console.WriteLine($"  State: {alarm.State}");
                Console.WriteLine($"  Enabled: {alarm.IsEnabled}");
                Console.WriteLine();
            }

            if (ShouldStopScheduler())
            {
                _logger.Information("Scheduler stopped by user");
                Console.WriteLine("Scheduler stopped.");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task<AlarmSession?> WaitForSessionResolutionAsync(Guid sessionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);

            if (session is not null &&
                (session.State == AlarmState.Completed ||
                 session.State == AlarmState.Failed))
            {
                return session;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return null;
    }

    private static bool ShouldStopScheduler()
    {
        if (Console.IsInputRedirected)
        {
            if (Console.In.Peek() < 0)
            {
                return false;
            }

            var input = Console.ReadLine()?.Trim().Trim('\uFEFF');
            return string.Equals(input, "Q", StringComparison.OrdinalIgnoreCase);
        }

        if (!Console.KeyAvailable)
        {
            return false;
        }

        var key = Console.ReadKey(intercept: true);
        return key.Key == ConsoleKey.Q;
    }
}
