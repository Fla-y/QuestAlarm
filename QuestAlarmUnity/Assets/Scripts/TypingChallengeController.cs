using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class TypingChallengeController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField hiddenInputField;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private TextMeshProUGUI resultText;

    [Header("Phrase")]
    [TextArea(2, 4)]
    [SerializeField] private string targetPhrase = "Today starts now.";
    [SerializeField] private ChallengeDifficulty difficulty = ChallengeDifficulty.Normal;
    [SerializeField] private bool blockOnError = true;

    [Header("Colors")]
    [SerializeField] private string correctHex = "#d1d0c5";
    [SerializeField] private string incorrectHex = "#ca4754";
    [SerializeField] private string pendingHex = "#646669";
    [SerializeField] private string currentHex = "#e2b714";

    private bool _completed;
    private TypingChallengeSettings _settings;
    private ChallengeLaunchContext _launchContext;
    private float _lastActivitySignalTime;

    private void Start()
    {
        _launchContext = ChallengeLaunchContext.FromCommandLine();
        LoadGameplayConfig(_launchContext.ConfigPath);

        _settings = BuildSettings(difficulty);
        hiddenInputField.SetTextWithoutNotify(string.Empty);
        hiddenInputField.onValueChanged.AddListener(HandleValueChanged);

        resultText.text = string.Empty;

        hiddenInputField.ActivateInputField();
        hiddenInputField.Select();

        RenderPrompt(string.Empty);
        UpdateStats(string.Empty);

        StartCoroutine(PostSessionCallback("challenge-running"));
    }

    private void OnDestroy()
    {
        hiddenInputField.onValueChanged.RemoveListener(HandleValueChanged);
    }

    private void Update()
    {
        if (!_completed && !hiddenInputField.isFocused)
        {
            hiddenInputField.ActivateInputField();
            hiddenInputField.Select();
        }
    }

    private void HandleValueChanged(string value)
    {
        if (_completed)
        {
            return;
        }

        if (blockOnError)
        {
            string corrected = EnforceValidPrefix(value);

            if (corrected != value)
            {
                hiddenInputField.SetTextWithoutNotify(corrected);
                hiddenInputField.caretPosition = corrected.Length;
                value = corrected;
            }
        }

        RenderPrompt(value);
        UpdateStats(value);
        TryPostActivitySignal();

        if (IsMatch(value, targetPhrase))
        {
            CompleteChallenge();
        }
    }

    private string EnforceValidPrefix(string input)
    {
        int validLength = 0;
        int max = Mathf.Min(input.Length, targetPhrase.Length);

        for (int i = 0; i < max; i++)
        {
            if (!CharsEqual(input[i], targetPhrase[i]))
            {
                break;
            }

            validLength++;
        }

        return input[..validLength];
    }

    private void RenderPrompt(string input)
    {
        var builder = new StringBuilder();
        int inputLength = Mathf.Min(input.Length, targetPhrase.Length);

        for (int i = 0; i < targetPhrase.Length; i++)
        {
            char targetChar = targetPhrase[i];
            string escaped = EscapeForTmp(targetChar);

            if (i < inputLength)
            {
                if (CharsEqual(input[i], targetChar))
                {
                    builder.Append($"<color={correctHex}>{escaped}</color>");
                }
                else
                {
                    builder.Append($"<color={incorrectHex}>{escaped}</color>");
                }
            }
            else if (i == inputLength)
            {
                builder.Append($"<color={currentHex}><u>{escaped}</u></color>");
            }
            else
            {
                builder.Append($"<color={pendingHex}>{escaped}</color>");
            }
        }

        promptText.text = builder.ToString();
    }

    private void UpdateStats(string input)
    {
        int correctChars = GetCorrectPrefixLength(input);
        statsText.text = $"{correctChars}/{targetPhrase.Length}";
    }

    private int GetCorrectPrefixLength(string input)
    {
        int max = Mathf.Min(input.Length, targetPhrase.Length);
        int count = 0;

        for (int i = 0; i < max; i++)
        {
            if (!CharsEqual(input[i], targetPhrase[i]))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private bool IsMatch(string input, string target)
    {
        if (input.Length != target.Length)
        {
            return false;
        }

        for (int i = 0; i < input.Length; i++)
        {
            if (!CharsEqual(input[i], target[i]))
            {
                return false;
            }
        }

        return true;
    }
    private bool CharsEqual(char a, char b)
    {
        if (_settings.IgnoreCase)
        {
            return char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
        }

        return a == b;
    }

    private void CompleteChallenge()
    {
        _completed = true;
        resultText.text = "Completed";
        hiddenInputField.interactable = false;

        Debug.Log("Typing challenge completed.");
        StartCoroutine(PostSessionCallback("completed", quitAfter: true));
    }

    private void LoadGameplayConfig(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            Debug.LogWarning("No --config argument was provided. Using inspector challenge settings.");
            return;
        }

        if (!File.Exists(configPath))
        {
            Debug.LogWarning($"Challenge config file was not found: {configPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            var config = JsonUtility.FromJson<TypingChallengeConfig>(json);

            if (!string.IsNullOrWhiteSpace(config.phrase))
            {
                targetPhrase = config.phrase;
            }

            if (!string.IsNullOrWhiteSpace(config.difficulty) &&
                Enum.TryParse(config.difficulty, ignoreCase: true, out ChallengeDifficulty parsedDifficulty))
            {
                difficulty = parsedDifficulty;
            }

            Debug.Log($"Loaded challenge config: {configPath}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not load challenge config '{configPath}': {ex.Message}");
        }
    }

    private void TryPostActivitySignal()
    {
        if (Time.unscaledTime - _lastActivitySignalTime < 1f)
        {
            return;
        }

        _lastActivitySignalTime = Time.unscaledTime;
        StartCoroutine(PostSessionCallback("activity"));
    }

    private IEnumerator PostSessionCallback(string endpoint, bool quitAfter = false)
    {
        if (!_launchContext.CanCallCallbacks)
        {
            if (quitAfter)
            {
                QuitApplication();
            }

            yield break;
        }

        string requestUri = $"{_launchContext.CallbackUrl.TrimEnd('/')}/api/sessions/{_launchContext.SessionId}/{endpoint}";
        byte[] body = Encoding.UTF8.GetBytes("{\"source\":\"QuestAlarmUnity\"}");

        using (var request = new UnityWebRequest(requestUri, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer()
        })
        {
            request.timeout = 5;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-QuestAlarm-Session-Token", _launchContext.ChallengeToken);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Challenge callback accepted: {endpoint}");
            }
            else
            {
                Debug.LogWarning($"Challenge callback failed: {endpoint} {request.responseCode} {request.error}");
            }
        }

        if (quitAfter)
        {
            QuitApplication();
        }
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static string EscapeForTmp(char c)
    {
        return c switch
        {
            '<' => "&lt;",
            '>' => "&gt;",
            '&' => "&amp;",
            _ => c.ToString()
        };
    }
    private static TypingChallengeSettings BuildSettings(ChallengeDifficulty difficulty)
    {
        return difficulty switch
        {
            ChallengeDifficulty.Easy => new TypingChallengeSettings
            {
                IgnoreCase = true
            },
            ChallengeDifficulty.Normal => new TypingChallengeSettings
            {
                IgnoreCase = false
            },
            ChallengeDifficulty.Hard => new TypingChallengeSettings
            {
                IgnoreCase = false
            },
            _ => new TypingChallengeSettings
            {
                IgnoreCase = false
            }
        };
    }

    [Serializable]
    private sealed class TypingChallengeConfig
    {
        public string phrase;
        public string difficulty;
    }

    private readonly struct ChallengeLaunchContext
    {
        private ChallengeLaunchContext(
            string configPath,
            string sessionId,
            string callbackUrl,
            string challengeToken)
        {
            ConfigPath = configPath;
            SessionId = sessionId;
            CallbackUrl = callbackUrl;
            ChallengeToken = challengeToken;
        }

        public string ConfigPath { get; }
        public string SessionId { get; }
        public string CallbackUrl { get; }
        public string ChallengeToken { get; }

        public bool CanCallCallbacks =>
            !string.IsNullOrWhiteSpace(SessionId) &&
            !string.IsNullOrWhiteSpace(CallbackUrl) &&
            !string.IsNullOrWhiteSpace(ChallengeToken);

        public static ChallengeLaunchContext FromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();

            return new ChallengeLaunchContext(
                GetArgumentValue(arguments, "config"),
                GetArgumentValue(arguments, "session-id"),
                GetArgumentValue(arguments, "callback-url"),
                GetArgumentValue(arguments, "challenge-token"));
        }

        private static string GetArgumentValue(string[] arguments, string name)
        {
            string flag = $"--{name}";

            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], flag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                return arguments[index + 1];
            }

            return string.Empty;
        }
    }
}
