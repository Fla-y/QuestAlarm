using System.Net.Http.Json;

var arguments = ParseArguments(args);

if (!arguments.TryGetValue("session-id", out var sessionId) ||
    !Guid.TryParse(sessionId, out _))
{
    Console.Error.WriteLine("Missing or invalid --session-id argument.");
    return 2;
}

if (!arguments.TryGetValue("alarm-id", out var alarmId) ||
    !Guid.TryParse(alarmId, out _))
{
    Console.Error.WriteLine("Missing or invalid --alarm-id argument.");
    return 2;
}

if (!arguments.TryGetValue("callback-url", out var callbackUrl) ||
    !Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callbackUri))
{
    Console.Error.WriteLine("Missing or invalid --callback-url argument.");
    return 2;
}

if (!arguments.TryGetValue("challenge-token", out var challengeToken) ||
    string.IsNullOrWhiteSpace(challengeToken))
{
    Console.Error.WriteLine("Missing --challenge-token argument.");
    return 2;
}

Console.WriteLine("QuestAlarm fake challenge client started.");
Console.WriteLine($"Session id:   {sessionId}");
Console.WriteLine($"Alarm id:     {alarmId}");
Console.WriteLine($"Callback URL: {callbackUri}");

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(10)
};

var runningResult = await PostSessionCallbackAsync(httpClient, callbackUri, sessionId, challengeToken, "challenge-running");

if (!runningResult)
{
    return 1;
}

var delay = GetCompletionDelay(arguments);

if (delay > TimeSpan.Zero)
{
    await Task.Delay(delay);
}

var completionEndpoint = arguments.TryGetValue("result", out var result) &&
    string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase)
        ? "failed"
        : "completed";

var completionResult = await PostSessionCallbackAsync(httpClient, callbackUri, sessionId, challengeToken, completionEndpoint);

return completionResult ? 0 : 1;

static async Task<bool> PostSessionCallbackAsync(
    HttpClient httpClient,
    Uri callbackUri,
    string sessionId,
    string challengeToken,
    string endpoint)
{
    const string challengeTokenHeaderName = "X-QuestAlarm-Session-Token";

    var requestUri = new Uri(callbackUri, $"/api/sessions/{sessionId}/{endpoint}");

    Console.WriteLine($"POST {requestUri}");

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new { source = "QuestAlarm.FakeChallengeClient" })
        };

        request.Headers.TryAddWithoutValidation(challengeTokenHeaderName, challengeToken);

        using var response = await httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Callback '{endpoint}' accepted.");
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"Callback '{endpoint}' failed with HTTP {(int)response.StatusCode}: {body}");
        return false;
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Callback '{endpoint}' failed: {ex.Message}");
        return false;
    }
    catch (TaskCanceledException ex)
    {
        Console.Error.WriteLine($"Callback '{endpoint}' timed out: {ex.Message}");
        return false;
    }
}

static TimeSpan GetCompletionDelay(IReadOnlyDictionary<string, string> arguments)
{
    if (!arguments.TryGetValue("delay-ms", out var delayInput) ||
        !int.TryParse(delayInput, out var delayMilliseconds) ||
        delayMilliseconds < 0)
    {
        return TimeSpan.FromMilliseconds(500);
    }

    return TimeSpan.FromMilliseconds(delayMilliseconds);
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < args.Length; index++)
    {
        var current = args[index];

        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = current[2..];

        if (string.IsNullOrWhiteSpace(key))
        {
            continue;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[key] = string.Empty;
            continue;
        }

        result[key] = args[index + 1];
        index++;
    }

    return result;
}
