using QuestAlarm.Desktop.Services;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuestAlarm.Desktop;

public partial class AlarmPopupWindow : Window
{
    private static readonly TimeSpan ActivitySignalThrottle = TimeSpan.FromSeconds(1);

    private readonly AlarmNotificationRequest _request;
    private readonly ChallengeLaunchService _challengeLaunchService;
    private readonly IChallengeActivityService _challengeActivityService;
    private readonly TimeSpan _inactivityTimeout;
    private readonly DispatcherTimer _soundTimer;
    private readonly DispatcherTimer _completedCloseTimer;
    private DateTime _lastActivitySignalUtc = DateTime.MinValue;
    private bool _isCompleted;
    private bool _isFailed;
    private bool _isLaunching;
    private bool _wasLaunched;
    private bool _isChallengeRunning;

    public AlarmPopupWindow(
        AlarmNotificationRequest request,
        ChallengeLaunchService challengeLaunchService,
        IChallengeActivityService challengeActivityService,
        TimeSpan inactivityTimeout)
    {
        InitializeComponent();

        _request = request ?? throw new ArgumentNullException(nameof(request));
        _challengeLaunchService = challengeLaunchService ?? throw new ArgumentNullException(nameof(challengeLaunchService));
        _challengeActivityService = challengeActivityService ?? throw new ArgumentNullException(nameof(challengeActivityService));
        _inactivityTimeout = inactivityTimeout;

        AlarmTitleText.Text = request.AlarmTitle;
        ScheduledForText.Text = $"Scheduled for: {request.ScheduledForLocal:yyyy-MM-dd HH:mm:ss}";
        TriggeredAtText.Text = $"Triggered at: {request.TriggeredAtLocal:yyyy-MM-dd HH:mm:ss}";
        SessionIdText.Text = $"Session: {request.SessionId:N}";
        SetWaitingForChallengeState();
        ActivityStatusText.Text = "No puzzle activity yet. Sound active until interaction is detected.";

        _soundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _soundTimer.Tick += (_, _) => UpdateAlarmSound();

        _completedCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _completedCloseTimer.Tick += (_, _) =>
        {
            _completedCloseTimer.Stop();
            Close();
        };

        Loaded += (_, _) =>
        {
            Focus();
            PlayAlarmSound();
            _soundTimer.Start();
        };

        Closed += (_, _) =>
        {
            _soundTimer.Stop();
            _completedCloseTimer.Stop();
        };

        PreviewKeyDown += (_, _) => RecordActivity("popup-keyboard");
        PreviewMouseDown += (_, _) => RecordActivity("popup-mouse");
        PreviewMouseMove += (_, _) => RecordActivity("popup-mouse");
    }

    public void MarkChallengeRunning()
    {
        if (_isCompleted)
        {
            return;
        }

        _isFailed = false;
        _isLaunching = false;
        _wasLaunched = true;
        _isChallengeRunning = true;
        StartQuestButton.IsEnabled = false;
        StartQuestButton.Content = "Quest Running";
        SetChallengeState(
            "Challenge running",
            "Keep interacting with the puzzle. The sound stays paused while activity continues.",
            "#DBEAFE",
            "#1D4ED8",
            "#BFDBFE");
    }

    public void MarkChallengeCompleted()
    {
        _isCompleted = true;
        _isFailed = false;
        _isChallengeRunning = false;
        StartQuestButton.IsEnabled = false;
        StartQuestButton.Content = "Completed";
        _soundTimer.Stop();
        SetChallengeState(
            "Completed",
            "Challenge completed. Alarm dismissed.",
            "#F0FDFA",
            "#0F766E",
            "#99F6E4");
        ActivityStatusText.Text = "Sound stopped. Closing popup...";
        _completedCloseTimer.Start();
    }

    public void MarkChallengeActivity(ChallengeActivitySnapshot activity)
    {
        if (_isCompleted || _isFailed || !_isChallengeRunning)
        {
            return;
        }

        UpdateActivityStatus(activity, DateTime.UtcNow);
    }

    public void MarkChallengeFailed()
    {
        if (_isCompleted)
        {
            return;
        }

        _isFailed = true;
        _isLaunching = false;
        _isChallengeRunning = false;
        StartQuestButton.IsEnabled = true;
        StartQuestButton.Content = "Retry Quest";
        SetChallengeState(
            "Failed",
            "Challenge failed. The alarm remains active.",
            "#FFF1F2",
            "#B91C1C",
            "#FECDD3");
        ActivityStatusText.Text = "Sound active after failed challenge.";
        PlayAlarmSound();
    }

    public void MarkChallengeLaunchFailed(string errorMessage)
    {
        if (_isCompleted)
        {
            return;
        }

        _isFailed = true;
        _isLaunching = false;
        _isChallengeRunning = false;
        StartQuestButton.IsEnabled = true;
        StartQuestButton.Content = "Retry Quest";
        SetChallengeState(
            "Challenge failed to start",
            errorMessage,
            "#FFF1F2",
            "#B91C1C",
            "#FECDD3");
        ActivityStatusText.Text = "Sound active. Check the challenge executable path in Settings.";
        PlayAlarmSound();
    }

    private static void PlayAlarmSound()
    {
        SystemSounds.Exclamation.Play();
    }

    private void RecordActivity(string source)
    {
        if (_isCompleted || _isFailed || !_isChallengeRunning)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastActivitySignalUtc < ActivitySignalThrottle)
        {
            return;
        }

        _lastActivitySignalUtc = nowUtc;
        var snapshot = _challengeActivityService.MarkActivity(_request.SessionId, source);
        UpdateActivityStatus(snapshot, nowUtc);
    }

    private void UpdateAlarmSound()
    {
        if (_isCompleted)
        {
            return;
        }

        if (_isFailed)
        {
            ActivityStatusText.Text = "Challenge failed. Sound active.";
            PlayAlarmSound();
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var snapshot = _challengeActivityService.GetActivity(_request.SessionId);
        if (snapshot is null)
        {
            ActivityStatusText.Text = "No puzzle activity yet. Sound active.";
            PlayAlarmSound();
            return;
        }

        var inactiveFor = nowUtc - snapshot.LastActivityUtc;
        UpdateActivityStatus(snapshot, nowUtc);

        if (inactiveFor < _inactivityTimeout)
        {
            return;
        }

        PlayAlarmSound();
    }

    private void UpdateActivityStatus(ChallengeActivitySnapshot snapshot, DateTime nowUtc)
    {
        var inactiveFor = nowUtc - snapshot.LastActivityUtc;
        if (inactiveFor < _inactivityTimeout)
        {
            var remainingSeconds = Math.Ceiling((_inactivityTimeout - inactiveFor).TotalSeconds);
            ActivityStatusText.Text = $"Puzzle activity detected. Sound paused for {remainingSeconds:0}s unless activity stops.";
            return;
        }

        ActivityStatusText.Text = $"No puzzle activity for {inactiveFor.TotalSeconds:0}s. Sound active.";
    }

    private void SetWaitingForChallengeState()
    {
        SetChallengeState(
            "Waiting for challenge",
            "QuestAlarm is waiting for the puzzle client to start.",
            "#FFFBEB",
            "#92400E",
            "#FDE68A");
    }

    private void SetChallengeState(
        string state,
        string description,
        string background,
        string foreground,
        string border)
    {
        ChallengeStateText.Text = state;
        ChallengeStateDescriptionText.Text = description;
        ChallengeStateBadge.Background = BrushFromHex(background);
        ChallengeStateBadge.BorderBrush = BrushFromHex(border);
        ChallengeStateText.Foreground = BrushFromHex(foreground);
    }

    private static Brush BrushFromHex(string value)
    {
        return (Brush)new BrushConverter().ConvertFromString(value)!;
    }

    private async void StartQuestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted || _isLaunching)
        {
            return;
        }

        _isFailed = false;
        _isLaunching = true;
        StartQuestButton.IsEnabled = false;
        StartQuestButton.Content = _wasLaunched ? "Retrying..." : "Starting...";
        ActivityStatusText.Text = "Starting quest...";

        try
        {
            var result = await _challengeLaunchService.LaunchAsync(_request.SessionId);

            if (!result.WasStarted)
            {
                MarkChallengeLaunchFailed(result.ErrorMessage ?? "Challenge client was not started.");
                return;
            }

            _wasLaunched = true;
            StartQuestButton.Content = "Quest Starting";
            ActivityStatusText.Text = "Quest launched. Waiting for activity...";
        }
        catch (Exception ex)
        {
            MarkChallengeLaunchFailed(ex.Message);
        }
        finally
        {
            _isLaunching = false;
        }
    }
}
