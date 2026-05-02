using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Enums;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Services;
using QuestAlarm.Core.ValueObjects;
using QuestAlarm.Infrastructure.Persistence;
using Serilog;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(context.Configuration);
});

var storagePaths = new StoragePaths(builder.Configuration["Storage:RootDirectory"]);
var alarmsDirectory = storagePaths.AlarmsDirectory;
var sessionsDirectory = storagePaths.SessionsDirectory;

builder.Services.AddSingleton<IAlarmRepository>(_ => new JsonAlarmRepository(alarmsDirectory));
builder.Services.AddSingleton<IAlarmSessionRepository>(_ => new JsonAlarmSessionRepository(sessionsDirectory));
builder.Services.AddSingleton<IAlarmSessionService, AlarmSessionService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "QuestAlarm.Api" }));

app.MapGet("/api/alarms", async (IAlarmRepository alarmRepository) =>
{
    var alarms = await alarmRepository.GetAllAsync();
    var response = alarms.Select(ToAlarmResponse);
    return Results.Ok(response);
});

app.MapGet("/api/alarms/{alarmId:guid}", async (Guid alarmId, IAlarmRepository alarmRepository) =>
{
    var alarm = await alarmRepository.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapPost("/api/alarms", async (CreateAlarmRequestDto request, IAlarmRepository alarmRepository) =>
{
    if (!TryBuildScheduleForCreate(request, out var schedule, out var validationError))
    {
        return Results.BadRequest(new { error = validationError });
    }

    Alarm alarm;
    try
    {
        alarm = new Alarm(
            Guid.NewGuid(),
            request.Title,
            schedule!,
            isEnabled: true,
            state: AlarmState.Scheduled,
            createdAtUtc: DateTime.UtcNow);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    await alarmRepository.SaveAsync(alarm);

    return Results.Created($"/api/alarms/{alarm.Id}", ToAlarmResponse(alarm));
});

app.MapPatch("/api/alarms/{alarmId:guid}", async (
    Guid alarmId,
    UpdateAlarmRequestDto request,
    IAlarmRepository alarmRepository) =>
{
    var alarm = await alarmRepository.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    if (!TryApplyAlarmUpdate(alarm, request, out var validationError))
    {
        return Results.BadRequest(new { error = validationError });
    }

    await alarmRepository.SaveAsync(alarm);

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapDelete("/api/alarms/{alarmId:guid}", async (Guid alarmId, IAlarmRepository alarmRepository) =>
{
    var alarm = await alarmRepository.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    await alarmRepository.DeleteAsync(alarmId);
    return Results.NoContent();
});

app.MapPost("/api/alarms/{alarmId:guid}/enable", async (Guid alarmId, IAlarmRepository alarmRepository) =>
{
    var alarm = await alarmRepository.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    alarm.Enable();
    await alarmRepository.SaveAsync(alarm);

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapPost("/api/alarms/{alarmId:guid}/disable", async (Guid alarmId, IAlarmRepository alarmRepository) =>
{
    var alarm = await alarmRepository.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    alarm.Disable();
    await alarmRepository.SaveAsync(alarm);

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapPost("/api/sessions/{sessionId:guid}/challenge-running", async (
    Guid sessionId,
    HttpRequest request,
    IAlarmRepository alarmRepository,
    IAlarmSessionRepository sessionRepository,
    IAlarmSessionService sessionService,
    ILogger<Program> logger) =>
{
    var context = await LoadSessionContextAsync(sessionId, alarmRepository, sessionRepository);

    if (context.Result is not null)
    {
        return context.Result;
    }

    var tokenResult = ValidateChallengeToken(context.Session!, request);

    if (tokenResult is not null)
    {
        return tokenResult;
    }

    try
    {
        sessionService.StartChallenge(context.Session!, context.Alarm!);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message, sessionId });
    }

    await sessionRepository.SaveAsync(context.Session!);
    await alarmRepository.SaveAsync(context.Alarm!);

    logger.LogInformation(
        "Session {SessionId} and alarm {AlarmId} marked challenge running",
        context.Session!.Id,
        context.Alarm!.Id);

    return Results.Ok(ToResponse(context.Session!, context.Alarm!));
});

app.MapPost("/api/sessions/{sessionId:guid}/completed", async (
    Guid sessionId,
    HttpRequest request,
    IAlarmRepository alarmRepository,
    IAlarmSessionRepository sessionRepository,
    IAlarmSessionService sessionService,
    ILogger<Program> logger) =>
{
    var context = await LoadSessionContextAsync(sessionId, alarmRepository, sessionRepository);

    if (context.Result is not null)
    {
        return context.Result;
    }

    var tokenResult = ValidateChallengeToken(context.Session!, request);

    if (tokenResult is not null)
    {
        return tokenResult;
    }

    try
    {
        sessionService.CompleteChallenge(context.Session!, context.Alarm!, DateTime.UtcNow);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message, sessionId });
    }

    await sessionRepository.SaveAsync(context.Session!);
    await alarmRepository.SaveAsync(context.Alarm!);

    logger.LogInformation(
        "Session {SessionId} and alarm {AlarmId} completed",
        context.Session!.Id,
        context.Alarm!.Id);

    return Results.Ok(ToResponse(context.Session!, context.Alarm!));
});

app.MapPost("/api/sessions/{sessionId:guid}/failed", async (
    Guid sessionId,
    HttpRequest request,
    IAlarmRepository alarmRepository,
    IAlarmSessionRepository sessionRepository,
    IAlarmSessionService sessionService,
    ILogger<Program> logger) =>
{
    var context = await LoadSessionContextAsync(sessionId, alarmRepository, sessionRepository);

    if (context.Result is not null)
    {
        return context.Result;
    }

    var tokenResult = ValidateChallengeToken(context.Session!, request);

    if (tokenResult is not null)
    {
        return tokenResult;
    }

    try
    {
        sessionService.FailChallenge(context.Session!, context.Alarm!);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message, sessionId });
    }

    await sessionRepository.SaveAsync(context.Session!);
    await alarmRepository.SaveAsync(context.Alarm!);

    logger.LogWarning(
        "Session {SessionId} and alarm {AlarmId} failed",
        context.Session!.Id,
        context.Alarm!.Id);

    return Results.Ok(ToResponse(context.Session!, context.Alarm!));
});

app.Run();

static async Task<SessionContext> LoadSessionContextAsync(
    Guid sessionId,
    IAlarmRepository alarmRepository,
    IAlarmSessionRepository sessionRepository)
{
    var session = await sessionRepository.GetByIdAsync(sessionId);

    if (session is null)
    {
        return new SessionContext(null, null, Results.NotFound(new { error = "Session was not found.", sessionId }));
    }

    var alarm = await alarmRepository.GetByIdAsync(session.AlarmId);

    if (alarm is null)
    {
        return new SessionContext(session, null, Results.NotFound(new { error = "Alarm was not found.", sessionId, session.AlarmId }));
    }

    return new SessionContext(session, alarm, null);
}

static IResult? ValidateChallengeToken(AlarmSession session, HttpRequest request)
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

static object ToResponse(AlarmSession session, Alarm alarm)
{
    return new
    {
        session = new
        {
            session.Id,
            session.AlarmId,
            session.TriggeredAtUtc,
            State = session.State.ToString(),
            session.CompletedAtUtc
        },
        alarm = new
        {
            alarm.Id,
            alarm.Title,
            State = alarm.State.ToString(),
            alarm.IsEnabled
        }
    };
}

static AlarmResponseDto ToAlarmResponse(Alarm alarm)
{
    var schedule = new AlarmScheduleResponseDto(
        alarm.Schedule.Time,
        alarm.Schedule.StartDate,
        alarm.Schedule.IsRecurring,
        alarm.Schedule.RecurringDays.Select(day => day.ToString()).ToArray());

    return new AlarmResponseDto(
        alarm.Id,
        alarm.Title,
        alarm.IsEnabled,
        alarm.State.ToString(),
        alarm.CreatedAtUtc,
        schedule);
}

static bool TryBuildScheduleForCreate(
    CreateAlarmRequestDto request,
    out AlarmSchedule? schedule,
    out string? validationError)
{
    schedule = null;
    validationError = null;

    if (string.IsNullOrWhiteSpace(request.Title))
    {
        validationError = "Title is required.";
        return false;
    }

    if (!TimeOnly.TryParse(request.Time, out var time))
    {
        validationError = "Time must be a valid value in HH:mm format.";
        return false;
    }

    if (request.IsRecurring)
    {
        var recurringDays = ParseRecurringDays(request.RecurringDays);
        if (recurringDays.Count == 0)
        {
            validationError = "Recurring alarms require at least one valid recurring day.";
            return false;
        }

        schedule = new AlarmSchedule(
            time,
            isRecurring: true,
            recurringDays: recurringDays);
        return true;
    }

    if (string.IsNullOrWhiteSpace(request.StartDate) ||
        !DateOnly.TryParse(request.StartDate, out var startDate))
    {
        validationError = "One-time alarms require a valid start date in yyyy-MM-dd format.";
        return false;
    }

    schedule = new AlarmSchedule(
        time,
        startDate: startDate,
        isRecurring: false);
    return true;
}

static bool TryApplyAlarmUpdate(Alarm alarm, UpdateAlarmRequestDto request, out string? validationError)
{
    validationError = null;

    if (request.Title is not null)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            validationError = "Title cannot be empty.";
            return false;
        }

        alarm.UpdateTitle(request.Title);
    }

    var hasScheduleChange =
        request.Time is not null ||
        request.StartDate is not null ||
        request.RecurringDays is not null;

    if (!hasScheduleChange)
    {
        return true;
    }

    if (alarm.Schedule.IsRecurring)
    {
        if (request.StartDate is not null)
        {
            validationError = "Recurring alarms do not support startDate updates.";
            return false;
        }

        var time = alarm.Schedule.Time;
        if (request.Time is not null && !TimeOnly.TryParse(request.Time, out time))
        {
            validationError = "Time must be a valid value in HH:mm format.";
            return false;
        }

        var recurringDays = alarm.Schedule.RecurringDays;
        if (request.RecurringDays is not null)
        {
            recurringDays = ParseRecurringDays(request.RecurringDays);
            if (recurringDays.Count == 0)
            {
                validationError = "Recurring alarms require at least one valid recurring day.";
                return false;
            }
        }

        alarm.UpdateSchedule(new AlarmSchedule(
            time,
            isRecurring: true,
            recurringDays: recurringDays));
    }
    else
    {
        if (request.RecurringDays is not null)
        {
            validationError = "One-time alarms do not support recurringDays updates.";
            return false;
        }

        var time = alarm.Schedule.Time;
        if (request.Time is not null && !TimeOnly.TryParse(request.Time, out time))
        {
            validationError = "Time must be a valid value in HH:mm format.";
            return false;
        }

        var startDate = alarm.Schedule.StartDate;
        if (request.StartDate is not null)
        {
            if (!DateOnly.TryParse(request.StartDate, out var parsedDate))
            {
                validationError = "StartDate must be a valid value in yyyy-MM-dd format.";
                return false;
            }

            startDate = parsedDate;
        }

        if (startDate is null)
        {
            validationError = "One-time alarms require a start date.";
            return false;
        }

        alarm.UpdateSchedule(new AlarmSchedule(
            time,
            startDate: startDate.Value,
            isRecurring: false));
    }

    if (alarm.IsEnabled)
    {
        alarm.Enable();
    }

    return true;
}

static IReadOnlyCollection<DayOfWeek> ParseRecurringDays(IReadOnlyCollection<string>? rawDays)
{
    if (rawDays is null || rawDays.Count == 0)
    {
        return Array.Empty<DayOfWeek>();
    }

    var result = new List<DayOfWeek>();

    foreach (var rawDay in rawDays)
    {
        if (TryParseDayOfWeek(rawDay, out var dayOfWeek) && !result.Contains(dayOfWeek))
        {
            result.Add(dayOfWeek);
        }
    }

    return result;
}

static bool TryParseDayOfWeek(string? value, out DayOfWeek dayOfWeek)
{
    dayOfWeek = default;

    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var normalized = value.Trim();

    if (int.TryParse(normalized, out var dayNumber))
    {
        dayOfWeek = dayNumber switch
        {
            1 => DayOfWeek.Monday,
            2 => DayOfWeek.Tuesday,
            3 => DayOfWeek.Wednesday,
            4 => DayOfWeek.Thursday,
            5 => DayOfWeek.Friday,
            6 => DayOfWeek.Saturday,
            7 => DayOfWeek.Sunday,
            _ => default
        };

        return dayNumber is >= 1 and <= 7;
    }

    return Enum.TryParse(normalized, ignoreCase: true, out dayOfWeek);
}

internal sealed record SessionContext(AlarmSession? Session, Alarm? Alarm, IResult? Result);
internal sealed record AlarmResponseDto(
    Guid Id,
    string Title,
    bool IsEnabled,
    string State,
    DateTime CreatedAtUtc,
    AlarmScheduleResponseDto Schedule);

internal sealed record AlarmScheduleResponseDto(
    TimeOnly Time,
    DateOnly? StartDate,
    bool IsRecurring,
    IReadOnlyCollection<string> RecurringDays);

internal sealed record CreateAlarmRequestDto(
    string Title,
    string Time,
    string? StartDate,
    bool IsRecurring,
    IReadOnlyCollection<string>? RecurringDays);

internal sealed record UpdateAlarmRequestDto(
    string? Title,
    string? Time,
    string? StartDate,
    IReadOnlyCollection<string>? RecurringDays);
