using System.Windows;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace QuestAlarm.Desktop.Services;

public sealed class WpfAlarmNotificationService : IAlarmNotificationService
{
    private static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, AlarmPopupWindow> _openPopupsBySession = new();
    private readonly IChallengeActivityService _challengeActivityService;
    private readonly TimeSpan _inactivityTimeout;
    private readonly ILogger _logger;

    public WpfAlarmNotificationService(
        IChallengeActivityService challengeActivityService,
        IConfiguration configuration,
        ILogger logger)
    {
        _challengeActivityService = challengeActivityService ?? throw new ArgumentNullException(nameof(challengeActivityService));
        _inactivityTimeout = ResolveInactivityTimeout(configuration);
        _logger = logger.ForContext<WpfAlarmNotificationService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ShowTriggeredAlarmAsync(
        AlarmNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return Task.CompletedTask;
        }

        dispatcher.BeginInvoke(() =>
        {
            var popup = new AlarmPopupWindow(
                request,
                _challengeActivityService,
                _inactivityTimeout);

            if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner)
            {
                popup.Owner = owner;
            }

            popup.Closed += (_, _) => _openPopupsBySession.TryRemove(request.SessionId, out _);
            _openPopupsBySession[request.SessionId] = popup;

            popup.Show();
            popup.Activate();

            _logger.Information(
                "Alarm popup shown {AlarmId} {AlarmTitle} {SessionId}",
                request.AlarmId,
                request.AlarmTitle,
                request.SessionId);
        });

        return Task.CompletedTask;
    }

    public Task MarkChallengeRunningAsync(Guid sessionId)
    {
        return UpdatePopupAsync(
            sessionId,
            popup => popup.MarkChallengeRunning(),
            "Alarm popup marked challenge running {SessionId}");
    }

    public Task MarkChallengeCompletedAsync(Guid sessionId)
    {
        return UpdatePopupAsync(
            sessionId,
            popup => popup.MarkChallengeCompleted(),
            "Alarm popup marked challenge completed {SessionId}");
    }

    public Task MarkChallengeFailedAsync(Guid sessionId)
    {
        return UpdatePopupAsync(
            sessionId,
            popup => popup.MarkChallengeFailed(),
            "Alarm popup marked challenge failed {SessionId}");
    }

    private Task UpdatePopupAsync(
        Guid sessionId,
        Action<AlarmPopupWindow> updatePopup,
        string logMessageTemplate)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return Task.CompletedTask;
        }

        dispatcher.BeginInvoke(() =>
        {
            if (!_openPopupsBySession.TryGetValue(sessionId, out var popup))
            {
                return;
            }

            updatePopup(popup);
            _logger.Information(logMessageTemplate, sessionId);
        });

        return Task.CompletedTask;
    }

    private static TimeSpan ResolveInactivityTimeout(IConfiguration configuration)
    {
        var seconds = configuration.GetValue(
            "ChallengeActivity:InactivityTimeoutSeconds",
            (int)DefaultInactivityTimeout.TotalSeconds);

        return seconds is < 1 or > 300
            ? DefaultInactivityTimeout
            : TimeSpan.FromSeconds(seconds);
    }
}
