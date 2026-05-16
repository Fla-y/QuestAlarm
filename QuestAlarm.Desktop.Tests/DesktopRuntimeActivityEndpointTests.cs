using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Services;
using QuestAlarm.Core.ValueObjects;
using QuestAlarm.Desktop.Services;
using Serilog.Core;

namespace QuestAlarm.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DesktopRuntimeActivityEndpointTests
{
    [TestMethod]
    public async Task ActivityEndpointWithValidTokenUpdatesActivity()
    {
        using var harness = RuntimeHarness.Create(AlarmState.Triggering);

        await harness.Runtime.StartAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{harness.CallbackUrl}/api/sessions/{harness.Session.Id}/activity");
        request.Headers.Add("X-QuestAlarm-Session-Token", harness.Session.ChallengeToken);

        var response = await harness.HttpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var activity = harness.ActivityService.GetActivity(harness.Session.Id);
        Assert.IsNotNull(activity);
        Assert.AreEqual("callback-api", activity.Source);
    }

    [TestMethod]
    public async Task ActivityEndpointWithoutTokenReturnsUnauthorized()
    {
        using var harness = RuntimeHarness.Create(AlarmState.Triggering);

        await harness.Runtime.StartAsync();

        var response = await harness.HttpClient.PostAsync(
            $"{harness.CallbackUrl}/api/sessions/{harness.Session.Id}/activity",
            content: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsNull(harness.ActivityService.GetActivity(harness.Session.Id));
    }

    [TestMethod]
    public async Task ActivityEndpointForCompletedSessionReturnsConflict()
    {
        using var harness = RuntimeHarness.Create(AlarmState.Completed);

        await harness.Runtime.StartAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{harness.CallbackUrl}/api/sessions/{harness.Session.Id}/activity");
        request.Headers.Add("X-QuestAlarm-Session-Token", harness.Session.ChallengeToken);

        var response = await harness.HttpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsNull(harness.ActivityService.GetActivity(harness.Session.Id));
    }

    [TestMethod]
    public async Task ChallengeRunningCallbackMarksActivityAndUpdatesPopup()
    {
        using var harness = RuntimeHarness.Create(AlarmState.Triggering);

        await harness.Runtime.StartAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{harness.CallbackUrl}/api/sessions/{harness.Session.Id}/challenge-running");
        request.Headers.Add("X-QuestAlarm-Session-Token", harness.Session.ChallengeToken);

        var response = await harness.HttpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(AlarmState.ChallengeRunning, harness.Session.State);
        Assert.AreEqual(AlarmState.ChallengeRunning, harness.Alarm.State);
        Assert.IsNotNull(harness.ActivityService.GetActivity(harness.Session.Id));
        Assert.AreEqual(1, harness.NotificationService.ChallengeRunningCount);
    }

    [TestMethod]
    public async Task CompletedCallbackClearsActivityAndUpdatesPopup()
    {
        using var harness = RuntimeHarness.Create(AlarmState.ChallengeRunning);
        harness.ActivityService.MarkActivity(harness.Session.Id, "test");

        await harness.Runtime.StartAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{harness.CallbackUrl}/api/sessions/{harness.Session.Id}/completed");
        request.Headers.Add("X-QuestAlarm-Session-Token", harness.Session.ChallengeToken);

        var response = await harness.HttpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(AlarmState.Completed, harness.Session.State);
        Assert.AreEqual(AlarmState.Completed, harness.Alarm.State);
        Assert.IsNull(harness.ActivityService.GetActivity(harness.Session.Id));
        Assert.AreEqual(1, harness.NotificationService.CompletedCount);
    }

    private sealed class RuntimeHarness : IDisposable
    {
        private RuntimeHarness(
            DesktopRuntimeService runtime,
            HttpClient httpClient,
            string callbackUrl,
            Alarm alarm,
            AlarmSession session,
            ChallengeActivityService activityService,
            RecordingAlarmNotificationService notificationService)
        {
            Runtime = runtime;
            HttpClient = httpClient;
            CallbackUrl = callbackUrl;
            Alarm = alarm;
            Session = session;
            ActivityService = activityService;
            NotificationService = notificationService;
        }

        public DesktopRuntimeService Runtime { get; }
        public HttpClient HttpClient { get; }
        public string CallbackUrl { get; }
        public Alarm Alarm { get; }
        public AlarmSession Session { get; }
        public ChallengeActivityService ActivityService { get; }
        public RecordingAlarmNotificationService NotificationService { get; }

        public static RuntimeHarness Create(AlarmState sessionState)
        {
            var callbackUrl = $"http://127.0.0.1:{GetFreeTcpPort()}";
            WriteRuntimeSettings(callbackUrl);

            var alarm = new Alarm(
                Guid.NewGuid(),
                "Runtime activity endpoint test alarm",
                new AlarmSchedule(
                    new TimeOnly(7, 30),
                    startDate: new DateOnly(2026, 5, 7),
                    isRecurring: false),
                isEnabled: true,
                state: AlarmState.Triggering,
                createdAtUtc: DateTime.UtcNow);

            var session = new AlarmSession(
                Guid.NewGuid(),
                alarm.Id,
                DateTime.UtcNow,
                sessionState,
                completedAtUtc: sessionState == AlarmState.Completed ? DateTime.UtcNow : null,
                challengeToken: "test-token");

            if (sessionState == AlarmState.Completed)
            {
                alarm.MarkCompleted();
            }
            else if (sessionState == AlarmState.ChallengeRunning)
            {
                alarm.MarkChallengeRunning();
            }

            var alarmRepository = new InMemoryAlarmRepository(alarm);
            var sessionRepository = new InMemoryAlarmSessionRepository(session);
            var activityService = new ChallengeActivityService(Logger.None);
            var notificationService = new RecordingAlarmNotificationService();

            var runtime = new DesktopRuntimeService(
                alarmRepository,
                sessionRepository,
                new FixedClock(DateTime.UtcNow, DateTime.Now),
                new AlarmDueEvaluator(new AlarmOccurrenceCalculator()),
                new AlarmScheduler(new AlarmDueEvaluator(new AlarmOccurrenceCalculator())),
                new AlarmTriggerService(),
                new AlarmMissedService(),
                new AlarmSessionService(),
                activityService,
                notificationService,
                new DesktopSettingsService(),
                Logger.None);

            return new RuntimeHarness(
                runtime,
                new HttpClient(),
                callbackUrl,
                alarm,
                session,
                activityService,
                notificationService);
        }

        public void Dispose()
        {
            HttpClient.Dispose();
            Runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void WriteRuntimeSettings(string callbackUrl)
        {
            var json = $$"""
            {
              "Storage": {
                "RootDirectory": "%TEMP%\\QuestAlarm.Tests"
              },
              "Development": {
                "ShowTestAlarmMenuOption": true
              },
              "Runtime": {
                "AutoStart": false
              },
              "ChallengeActivity": {
                "InactivityTimeoutSeconds": 10
              },
              "ChallengeClient": {
                "ExecutablePath": "",
                "ArgumentsTemplate": "",
                "CallbackUrl": "{{callbackUrl}}",
                "WorkingDirectory": ""
              }
            }
            """;

            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                json);
        }
    }

    private sealed class InMemoryAlarmRepository : IAlarmRepository
    {
        private readonly ConcurrentDictionary<Guid, Alarm> _alarms = new();

        public InMemoryAlarmRepository(params Alarm[] alarms)
        {
            foreach (var alarm in alarms)
            {
                _alarms[alarm.Id] = alarm;
            }
        }

        public Task<IReadOnlyCollection<Alarm>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<Alarm>>(_alarms.Values.ToList());
        }

        public Task<Alarm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_alarms.GetValueOrDefault(id));
        }

        public Task SaveAsync(Alarm alarm, CancellationToken cancellationToken = default)
        {
            _alarms[alarm.Id] = alarm;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _alarms.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAlarmSessionRepository : IAlarmSessionRepository
    {
        private readonly ConcurrentDictionary<Guid, AlarmSession> _sessions = new();

        public InMemoryAlarmSessionRepository(params AlarmSession[] sessions)
        {
            foreach (var session in sessions)
            {
                _sessions[session.Id] = session;
            }
        }

        public Task<IReadOnlyCollection<AlarmSession>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<AlarmSession>>(_sessions.Values.ToList());
        }

        public Task<AlarmSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_sessions.GetValueOrDefault(id));
        }

        public Task SaveAsync(AlarmSession session, CancellationToken cancellationToken = default)
        {
            _sessions[session.Id] = session;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpChallengeClientLauncher : IChallengeClientLauncher
    {
        public Task<ChallengeClientLaunchResult> LaunchAsync(
            AlarmSession session,
            Alarm alarm,
            string? challengeConfigPath = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ChallengeClientLaunchResult.NotStarted("Test launcher does not start processes."));
        }
    }

    private sealed class RecordingAlarmNotificationService : IAlarmNotificationService
    {
        public int ChallengeRunningCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int FailedCount { get; private set; }
        public int LaunchFailedCount { get; private set; }

        public Task ShowTriggeredAlarmAsync(
            AlarmNotificationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task MarkChallengeRunningAsync(Guid sessionId)
        {
            ChallengeRunningCount++;
            return Task.CompletedTask;
        }

        public Task MarkChallengeActivityAsync(ChallengeActivitySnapshot activity)
        {
            return Task.CompletedTask;
        }

        public Task MarkChallengeCompletedAsync(Guid sessionId)
        {
            CompletedCount++;
            return Task.CompletedTask;
        }

        public Task MarkChallengeFailedAsync(Guid sessionId)
        {
            FailedCount++;
            return Task.CompletedTask;
        }

        public Task MarkChallengeLaunchFailedAsync(Guid sessionId, string errorMessage)
        {
            LaunchFailedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow, DateTime localNow)
        {
            UtcNow = utcNow;
            LocalNow = localNow;
        }

        public DateTime UtcNow { get; }
        public DateTime LocalNow { get; }
    }
}
