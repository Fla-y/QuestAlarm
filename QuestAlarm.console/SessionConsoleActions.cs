using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;
using Serilog;

namespace QuestAlarm.ConsoleHost;

public sealed class SessionConsoleActions
{
    private readonly IAlarmRepository _alarmRepository;
    private readonly IAlarmSessionRepository _sessionRepository;
    private readonly IAlarmSessionService _sessionService;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public SessionConsoleActions(
        IAlarmRepository alarmRepository,
        IAlarmSessionRepository sessionRepository,
        IAlarmSessionService sessionService,
        IClock clock,
        ILogger logger)
    {
        _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ListSessionsAsync()
    {
        var sessions = await GetOrderedSessionsAsync();

        if (sessions.Count == 0)
        {
            Console.WriteLine("No alarm sessions found.");
            return;
        }

        Console.WriteLine("Saved sessions:");
        foreach (var session in sessions)
        {
            WriteSessionDetails(session);
            Console.WriteLine(new string('-', 50));
        }
    }

    public async Task StartChallengeAsync()
    {
        var session = await SelectSessionAsync("Select a session to mark as challenge running:");

        if (session is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var alarm = await GetSessionAlarmAsync(session);

        if (alarm is null)
        {
            return;
        }

        try
        {
            _sessionService.StartChallenge(session, alarm);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warning(ex, "Could not mark session {SessionId} challenge running", session.Id);
            Console.WriteLine(ex.Message);
            return;
        }

        await _sessionRepository.SaveAsync(session);
        await _alarmRepository.SaveAsync(alarm);

        _logger.Information("Session {SessionId} marked challenge running for alarm {AlarmId}", session.Id, session.AlarmId);

        Console.WriteLine($"Session '{session.Id}' marked as challenge running.");
    }

    public async Task CompleteSessionAsync()
    {
        var session = await SelectSessionAsync("Select a session to complete:");

        if (session is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var alarm = await GetSessionAlarmAsync(session);

        if (alarm is null)
        {
            return;
        }

        try
        {
            _sessionService.CompleteChallenge(session, alarm, _clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warning(ex, "Could not complete session {SessionId}", session.Id);
            Console.WriteLine(ex.Message);
            return;
        }

        await _sessionRepository.SaveAsync(session);
        await _alarmRepository.SaveAsync(alarm);

        _logger.Information("Session {SessionId} completed for alarm {AlarmId}", session.Id, session.AlarmId);

        Console.WriteLine($"Session '{session.Id}' completed.");
    }

    public async Task FailSessionAsync()
    {
        var session = await SelectSessionAsync("Select a session to fail:");

        if (session is null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var alarm = await GetSessionAlarmAsync(session);

        if (alarm is null)
        {
            return;
        }

        try
        {
            _sessionService.FailChallenge(session, alarm);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warning(ex, "Could not fail session {SessionId}", session.Id);
            Console.WriteLine(ex.Message);
            return;
        }

        await _sessionRepository.SaveAsync(session);
        await _alarmRepository.SaveAsync(alarm);

        _logger.Warning("Session {SessionId} failed for alarm {AlarmId}", session.Id, session.AlarmId);

        Console.WriteLine($"Session '{session.Id}' failed.");
    }

    private async Task<AlarmSession?> SelectSessionAsync(string prompt)
    {
        var sessions = await GetOrderedSessionsAsync();

        if (sessions.Count == 0)
        {
            Console.WriteLine("No alarm sessions found.");
            return null;
        }

        Console.WriteLine(prompt);

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            Console.WriteLine($"{index + 1}. {session.Id} | Alarm: {session.AlarmId} | State: {session.State} | Triggered: {session.TriggeredAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        }

        Console.Write("Choice: ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out var selectedIndex) ||
            selectedIndex < 1 ||
            selectedIndex > sessions.Count)
        {
            return null;
        }

        return sessions[selectedIndex - 1];
    }

    private async Task<List<AlarmSession>> GetOrderedSessionsAsync()
    {
        return (await _sessionRepository.GetAllAsync())
            .OrderByDescending(s => s.TriggeredAtUtc)
            .ToList();
    }

    private async Task<Alarm?> GetSessionAlarmAsync(AlarmSession session)
    {
        var alarm = await _alarmRepository.GetByIdAsync(session.AlarmId);

        if (alarm is not null)
        {
            return alarm;
        }

        _logger.Error("Could not update session {SessionId} because alarm {AlarmId} was not found", session.Id, session.AlarmId);
        Console.WriteLine($"Alarm '{session.AlarmId}' was not found for this session.");

        return null;
    }

    private static void WriteSessionDetails(AlarmSession session)
    {
        Console.WriteLine($"Id:             {session.Id}");
        Console.WriteLine($"Alarm id:       {session.AlarmId}");
        Console.WriteLine($"Triggered UTC:  {session.TriggeredAtUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"State:          {session.State}");
        Console.WriteLine($"Completed UTC:  {session.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}");
    }
}
