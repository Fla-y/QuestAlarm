using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using Serilog;

namespace QuestAlarm.Desktop.Services;

public sealed class ChallengeLaunchService
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmSessionRepository _sessionRepository;
    private readonly IChallengeClientLauncher _challengeClientLauncher;
    private readonly ChallengeConfigService _challengeConfigService;
    private readonly DesktopSettingsService _settingsService;
    private readonly ILogger _logger;

    public ChallengeLaunchService(
        IAlarmRepository alarmRepository,
        IAlarmSessionRepository sessionRepository,
        IChallengeClientLauncher challengeClientLauncher,
        ChallengeConfigService challengeConfigService,
        DesktopSettingsService settingsService,
        ILogger logger)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _challengeClientLauncher = challengeClientLauncher ?? throw new ArgumentNullException(nameof(challengeClientLauncher));
        _challengeConfigService = challengeConfigService ?? throw new ArgumentNullException(nameof(challengeConfigService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger.ForContext<ChallengeLaunchService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChallengeLaunchRequestResult> LaunchAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ChallengeLaunchRequestResult.NotStarted("Session was not found.");
        }

        if (session.State is AlarmState.Completed or AlarmState.Failed)
        {
            return ChallengeLaunchRequestResult.NotStarted($"Session is already {session.State}.");
        }

        var alarm = await _alarmRepository.GetByIdAsync(session.AlarmId, cancellationToken);
        if (alarm is null)
        {
            return ChallengeLaunchRequestResult.NotStarted("Alarm was not found.");
        }

        var settings = await _settingsService.LoadAsync(cancellationToken);
        var challengeConfigPath = await _challengeConfigService.CreateConfigAsync(
            settings.Storage.RootDirectory,
            session,
            alarm,
            cancellationToken);

        var launchResult = await _challengeClientLauncher.LaunchAsync(
            session,
            alarm,
            challengeConfigPath,
            cancellationToken);

        if (!launchResult.WasStarted)
        {
            var errorMessage = launchResult.ErrorMessage ?? "Challenge client was not started.";
            _logger.Warning(
                "Challenge client was not started {AlarmId} {AlarmTitle} {SessionId} {ChallengeConfigPath} {ErrorMessage}",
                alarm.Id,
                alarm.Title,
                session.Id,
                challengeConfigPath,
                errorMessage);

            return ChallengeLaunchRequestResult.NotStarted(errorMessage);
        }

        _logger.Information(
            "Challenge client launched {AlarmId} {AlarmTitle} {SessionId} {ChallengeConfigPath} {ProcessId}",
            alarm.Id,
            alarm.Title,
            session.Id,
            challengeConfigPath,
            launchResult.ProcessId);

        return ChallengeLaunchRequestResult.Started(launchResult.ProcessId, challengeConfigPath);
    }
}

public sealed record ChallengeLaunchRequestResult(
    bool WasStarted,
    int? ProcessId,
    string? ChallengeConfigPath,
    string? ErrorMessage)
{
    public static ChallengeLaunchRequestResult Started(int? processId, string challengeConfigPath)
    {
        return new ChallengeLaunchRequestResult(true, processId, challengeConfigPath, null);
    }

    public static ChallengeLaunchRequestResult NotStarted(string errorMessage)
    {
        return new ChallengeLaunchRequestResult(false, null, null, errorMessage);
    }
}
