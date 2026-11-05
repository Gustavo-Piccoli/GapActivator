using System.Text.RegularExpressions;

namespace GapActivator.App.Services;

/// <summary>The input rules of the executable field: the service does not expand variables, and a pasted path often comes quoted.</summary>
internal static partial class ExecutableInput
{
    /// <summary>Trims the text; when it then starts and ends with a quote, drops that one pair and trims again.</summary>
    public static string Normalize(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1].Trim();
        }

        return value;
    }

    /// <summary>True for a <c>%NAME%</c> pattern, which the service would take literally.</summary>
    public static bool HasVariablePattern(string? text) => !string.IsNullOrEmpty(text) && VariablePattern().IsMatch(text);

    [GeneratedRegex(@"%[^%\s]+%", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
