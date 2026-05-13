using System.Text;
using TMPro;
using UnityEngine;

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
    [SerializeField] private bool ignoreCase = true;
    [SerializeField] private bool blockOnError = true;

    [Header("Colors")]
    [SerializeField] private string correctHex = "#d1d0c5";
    [SerializeField] private string incorrectHex = "#ca4754";
    [SerializeField] private string pendingHex = "#646669";
    [SerializeField] private string currentHex = "#e2b714";

    private bool _completed;

    private void Start()
    {
        hiddenInputField.SetTextWithoutNotify(string.Empty);
        hiddenInputField.onValueChanged.AddListener(HandleValueChanged);

        resultText.text = string.Empty;

        hiddenInputField.ActivateInputField();
        hiddenInputField.Select();

        RenderPrompt(string.Empty);
        UpdateStats(string.Empty);
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
        if (ignoreCase)
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
}