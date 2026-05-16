using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace QuestAlarm.Desktop.Services;

public sealed class DesktopRuntimeService : IAsyncDisposable
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmSessionRepository _sessionRepository;
    private readonly IClock _clock;
    private readonly IAlarmDueEvaluator _dueEvaluator;
    private readonly IAlarmScheduler _scheduler;
    private readonly IAlarmTriggerService _triggerService;
    private readonly IAlarmMissedService _missedService;
    private readonly IAlarmSessionService _sessionService;
    private readonly IChallengeActivityService _challengeActivityService;
    private readonly IAlarmNotificationService _alarmNotificationService;
    private readonly DesktopSettingsService _settingsService;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runtimeCancellation;
    private Task? _schedulerTask;
    private WebApplication? _callbackApi;

    public DesktopRuntimeService(
        IAlarmRepository alarmRepository,
        IAlarmSessionRepository sessionRepository,
        IClock clock,
        IAlarmDueEvaluator dueEvaluator,
        IAlarmScheduler scheduler,
        IAlarmTriggerService triggerService,
        IAlarmMissedService missedService,
        IAlarmSessionService sessionService,
        IChallengeActivityService challengeActivityService,
        IAlarmNotificationService alarmNotificationService,
        DesktopSettingsService settingsService,
        ILogger logger)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dueEvaluator = dueEvaluator ?? throw new ArgumentNullException(nameof(dueEvaluator));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _missedService = missedService ?? throw new ArgumentNullException(nameof(missedService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _challengeActivityService = challengeActivityService ?? throw new ArgumentNullException(nameof(challengeActivityService));
        _alarmNotificationService = alarmNotificationService ?? throw new ArgumentNullException(nameof(alarmNotificationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger.ForContext<DesktopRuntimeService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRunning => _runtimeCancellation is { IsCancellationRequested: false };
    public string CallbackUrl { get; private set; } = "http://localhost:5055";
    public DateTime? StartedAtLocal { get; private set; }
    public string? LastEvent { get; private set; }
    public string? LastError { get; private set; }

    public async Task<Alarm> CreateTestAlarmDueSoonAsync(CancellationToken cancellationToken = default)
    {
        var dueAtLocal = DateTime.Now.AddMinutes(1);
        dueAtLocal = new DateTime(
            dueAtLocal.Year,
            dueAtLocal.Month,
            dueAtLocal.Day,
            dueAtLocal.Hour,
            dueAtLocal.Minute,
            0,
            DateTimeKind.Local);

        var alarm = new Alarm(
            Guid.NewGuid(),
            $"Desktop test alarm {dueAtLocal:HH:mm}",
            new AlarmSchedule(
                TimeOnly.FromDateTime(dueAtLocal),
                startDate: DateOnly.FromDateTime(dueAtLocal),
                isRecurring: false),
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: _clock.UtcNow);

        await _alarmRepository.SaveAsync(alarm, cancellationToken);

        LastEvent = $"Created test alarm due at {dueAtLocal:HH:mm}.";
        return alarm;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var settings = await _settingsService.LoadAsync(cancellationToken);
            CallbackUrl = settings.ChallengeClient.CallbackUrl;
            _runtimeCancellation = new CancellationTokenSource();
            _callbackApi = BuildCallbackApi(CallbackUrl);

            await _callbackApi.StartAsync(cancellationToken);

            _schedulerTask = Task.Run(
                () => RunSchedulerLoopAsync(_runtimeCancellation.Token),
                CancellationToken.None);

            StartedAtLocal = DateTime.Now;
            LastError = null;
            LastEvent = "Runtime started.";
            _logger.Information("Desktop runtime started {CallbackUrl}", CallbackUrl);
        }
        catch
        {
            await StopInternalAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopInternalAsync();
            LastEvent = "Runtime stopped.";
            _logger.Information("Desktop runtime stopped");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private WebApplication BuildCallbackApi(string callbackUrl)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(callbackUrl);

        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "QuestAlarm.Desktop" }));
        app.MapPost("/api/sessions/{sessionId:guid}/challenge-running", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionCallbackAsync(sessionId, request, CallbackAction.StartChallenge));
        app.MapPost("/api/sessions/{sessionId:guid}/activity", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionActivityAsync(sessionId, request));
        app.MapPost("/api/sessions/{sessionId:guid}/completed", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionCallbackAsync(sessionId, request, CallbackAction.CompleteChallenge));
        app.MapPost("/api/sessions/{sessionId:guid}/failed", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionCallbackAsync(sessionId, request, CallbackAction.FailChallenge));

        return app;
    }

    private async Task<IResult> HandleSessionActivityAsync(Guid sessionId, HttpRequest request)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session is null)
        {
            return Results.NotFound(new { error = "Session was not found.", sessionId });
        }

        var tokenResult = ValidateChallengeToken(session, request);
        if (tokenResult is not null)
        {
            return tokenResult;
        }

        if (session.State is AlarmState.Completed or AlarmState.Failed)
        {
            return Results.Conflict(new
            {
                error = "Session is already finished.",
                sessionId,
                State = session.State.ToString()
            });
        }

        var snapshot = _challengeActivityService.MarkActivity(session.Id, "callback-api");
        await _alarmNotificationService.MarkChallengeActivityAsync(snapshot);

        return Results.Ok(new
        {
            sessionId = snapshot.SessionId,
            snapshot.LastActivityUtc,
            snapshot.Source
        });
    }

    private async Task<IResult> HandleSessionCallbackAsync(
        Guid sessionId,
        HttpRequest request,
        CallbackAction action)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session is null)
        {
            return Results.NotFound(new { error = "Session was not found.", sessionId });
        }

        var alarm = await _alarmRepository.GetByIdAsync(session.AlarmId);
        if (alarm is null)
        {
            return Results.NotFound(new { error = "Alarm was not found.", sessionId, session.AlarmId });
        }

        var tokenResult = ValidateChallengeToken(session, request);
        if (tokenResult is not null)
        {
            return tokenResult;
        }

        try
        {
            switch (action)
            {
                case CallbackAction.StartChallenge:
                    _sessionService.StartChallenge(session, alarm);
                    _challengeActivityService.MarkActivity(session.Id, "challenge-running");
                    await _alarmNotificationService.MarkChallengeRunningAsync(session.Id);
                    _logger.Information(
                        "Session challenge running {SessionId} {AlarmId} {AlarmTitle}",
                        session.Id,
                        alarm.Id,
                        alarm.Title);
                    break;
                case CallbackAction.CompleteChallenge:
                    _sessionService.CompleteChallenge(session, alarm, _clock.UtcNow);
                    _challengeActivityService.ClearActivity(session.Id);
                    await _alarmNotificationService.MarkChallengeCompletedAsync(session.Id);
                    _logger.Information(
                        "Session completed {SessionId} {AlarmId} {AlarmTitle}",
                        session.Id,
                        alarm.Id,
                        alarm.Title);
                    break;
                case CallbackAction.FailChallenge:
                    _sessionService.FailChallenge(session, alarm);
                    _challengeActivityService.ClearActivity(session.Id);
                    await _alarmNotificationService.MarkChallengeFailedAsync(session.Id);
                    _logger.Warning(
                        "Session failed {SessionId} {AlarmId} {AlarmTitle}",
                        session.Id,
                        alarm.Id,
                        alarm.Title);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message, sessionId });
        }

        await _sessionRepository.SaveAsync(session);
        await _alarmRepository.SaveAsync(alarm);

        LastEvent = $"Session {session.Id:N} marked {session.State}.";
        return Results.Ok(new
        {
            session = new
            {
                session.Id,
                session.AlarmId,
                State = session.State.ToString(),
                session.TriggeredAtUtc,
                session.CompletedAtUtc
            },
            alarm = new
            {
                alarm.Id,
                alarm.Title,
                State = alarm.State.ToString(),
                alarm.IsEnabled
            }
        });
    }

    private async Task RunSchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var alarms = (await _alarmRepository.GetAllAsync(cancellationToken))
                    .Where(alarm => alarm.IsEnabled)
                    .ToList();

                var now = _clock.LocalNow;
                var dueTriggers = _scheduler.GetDueAlarms(alarms, now);

                foreach (var trigger in dueTriggers)
                {
                    var session = _triggerService.Trigger(trigger, _clock.UtcNow);

                    await _sessionRepository.SaveAsync(session, cancellationToken);
                    await _alarmRepository.SaveAsync(trigger.Alarm, cancellationToken);
                    _logger.Information(
                        "Alarm triggered {AlarmId} {AlarmTitle} {SessionId} {OccurrenceLocalDateTime}",
                        trigger.Alarm.Id,
                        trigger.Alarm.Title,
                        session.Id,
                        trigger.Occurrence.OccurrenceLocalDateTime);

                    await _alarmNotificationService.ShowTriggeredAlarmAsync(
                        new AlarmNotificationRequest(
                            trigger.Alarm.Id,
                            trigger.Alarm.Title,
                            trigger.Occurrence.OccurrenceLocalDateTime,
                            now,
                            session.Id),
                        cancellationToken);

                    LastError = null;
                    LastEvent = $"Triggered {trigger.Alarm.Title}.";
                }

                foreach (var alarm in alarms.Where(alarm => alarm.IsEnabled && !alarm.Schedule.IsRecurring))
                {
                    var evaluation = _dueEvaluator.Evaluate(alarm, now);

                    if (evaluation.Status != AlarmDueStatus.Missed)
                    {
                        continue;
                    }

                    _missedService.MarkMissed(alarm);
                    await _alarmRepository.SaveAsync(alarm, cancellationToken);
                    _logger.Warning(
                        "One-time alarm marked missed {AlarmId} {AlarmTitle}",
                        alarm.Id,
                        alarm.Title);

                    LastEvent = $"Marked {alarm.Title} as missed.";
                }

                LastError = null;
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.Error(ex, "Desktop scheduler loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task StopInternalAsync()
    {
        _runtimeCancellation?.Cancel();

        if (_schedulerTask is not null)
        {
            try
            {
                await _schedulerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_callbackApi is not null)
        {
            await _callbackApi.StopAsync();
            await _callbackApi.DisposeAsync();
        }

        _callbackApi = null;
        _schedulerTask = null;
        _runtimeCancellation?.Dispose();
        _runtimeCancellation = null;
        StartedAtLocal = null;
    }

    private static IResult? ValidateChallengeToken(AlarmSession session, HttpRequest request)
    {
        const string challengeTokenHeaderName = "X-QuestAlarm-Session-Token";

        if (string.IsNullOrWhiteSpace(session.ChallengeToken) ||
            !request.Headers.TryGetValue(challengeTokenHeaderName, out var headerValues))
        {
            return Results.Json(
                new { error = "Missing or invalid challenge token.", session.Id },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var expectedToken = Encoding.UTF8.GetBytes(session.ChallengeToken);
        var providedToken = Encoding.UTF8.GetBytes(headerValues.ToString());

        if (!CryptographicOperations.FixedTimeEquals(expectedToken, providedToken))
        {
            return Results.Json(
                new { error = "Missing or invalid challenge token.", session.Id },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return null;
    }

    private enum CallbackAction
    {
        StartChallenge,
        CompleteChallenge,
        FailChallenge
    }
}
