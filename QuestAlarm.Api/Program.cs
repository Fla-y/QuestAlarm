using QuestAlarm.Application.Alarms;
using QuestAlarm.Core.Entities;
using QuestAlarm.Core.Interfaces;
using QuestAlarm.Core.Services;
using QuestAlarm.Infrastructure.Persistence;
using QuestAlarm.Infrastructure.Time;
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
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAlarmSessionService, AlarmSessionService>();
builder.Services.AddSingleton<IAlarmManagementService, AlarmManagementService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "QuestAlarm.Api" }));

app.MapGet("/api/alarms", async (IAlarmManagementService alarmManagementService) =>
{
    var alarms = await alarmManagementService.GetAlarmsAsync();
    var response = alarms.Select(ToAlarmResponse);
    return Results.Ok(response);
});

app.MapGet("/api/alarms/{alarmId:guid}", async (
    Guid alarmId,
    IAlarmManagementService alarmManagementService) =>
{
    var alarm = await alarmManagementService.GetByIdAsync(alarmId);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapPost("/api/alarms", async (
    CreateAlarmRequestDto request,
    IAlarmManagementService alarmManagementService) =>
{
    var result = await alarmManagementService.CreateAsync(new CreateAlarmCommand(
        request.Title,
        request.Time,
        request.StartDate,
        request.IsRecurring,
        request.RecurringDays));

    if (!result.Succeeded)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    return Results.Created($"/api/alarms/{result.Alarm!.Id}", ToAlarmResponse(result.Alarm));
});

app.MapPatch("/api/alarms/{alarmId:guid}", async (
    Guid alarmId,
    UpdateAlarmRequestDto request,
    IAlarmManagementService alarmManagementService) =>
{
    var result = await alarmManagementService.UpdateAsync(
        alarmId,
        new UpdateAlarmCommand(
            request.Title,
            request.Time,
            request.StartDate,
            request.RecurringDays));

    if (!result.Succeeded && result.ErrorKind == AlarmMutationErrorKind.NotFound)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    if (!result.Succeeded)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    return Results.Ok(ToAlarmResponse(result.Alarm!));
});

app.MapDelete("/api/alarms/{alarmId:guid}", async (
    Guid alarmId,
    IAlarmManagementService alarmManagementService) =>
{
    var wasDeleted = await alarmManagementService.DeleteAsync(alarmId);

    if (!wasDeleted)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    return Results.NoContent();
});

app.MapPost("/api/alarms/{alarmId:guid}/enable", async (
    Guid alarmId,
    IAlarmManagementService alarmManagementService) =>
{
    var alarm = await alarmManagementService.SetEnabledAsync(alarmId, isEnabled: true);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

    return Results.Ok(ToAlarmResponse(alarm));
});

app.MapPost("/api/alarms/{alarmId:guid}/disable", async (
    Guid alarmId,
    IAlarmManagementService alarmManagementService) =>
{
    var alarm = await alarmManagementService.SetEnabledAsync(alarmId, isEnabled: false);

    if (alarm is null)
    {
        return Results.NotFound(new { error = "Alarm was not found.", alarmId });
    }

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
