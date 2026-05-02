using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

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
    private readonly IChallengeClientLauncher _challengeClientLauncher;
    private readonly DesktopSettingsService _settingsService;

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
        IChallengeClientLauncher challengeClientLauncher,
        DesktopSettingsService settingsService)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dueEvaluator = dueEvaluator ?? throw new ArgumentNullException(nameof(dueEvaluator));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _missedService = missedService ?? throw new ArgumentNullException(nameof(missedService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _challengeClientLauncher = challengeClientLauncher ?? throw new ArgumentNullException(nameof(challengeClientLauncher));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public bool IsRunning => _runtimeCancellation is { IsCancellationRequested: false };
    public string CallbackUrl { get; private set; } = "http://localhost:5055";
    public DateTime? StartedAtLocal { get; private set; }
    public string? LastEvent { get; private set; }
    public string? LastError { get; private set; }

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
        app.MapPost("/api/sessions/{sessionId:guid}/completed", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionCallbackAsync(sessionId, request, CallbackAction.CompleteChallenge));
        app.MapPost("/api/sessions/{sessionId:guid}/failed", async (Guid sessionId, HttpRequest request) =>
            await HandleSessionCallbackAsync(sessionId, request, CallbackAction.FailChallenge));

        return app;
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
                    break;
                case CallbackAction.CompleteChallenge:
                    _sessionService.CompleteChallenge(session, alarm, _clock.UtcNow);
                    break;
                case CallbackAction.FailChallenge:
                    _sessionService.FailChallenge(session, alarm);
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
                    await _challengeClientLauncher.LaunchAsync(session, trigger.Alarm, cancellationToken);

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
