using System.Text;

namespace GapActivator.Service.Ipc;

/// <summary>Cleans the text of a notification before it reaches the log or a client.</summary>
internal static class NotificationText
{
    /// <summary>The longest client-influenced reason (a protocol violation, an unknown type, an error message) that is logged or echoed.</summary>
    public const int MaxReasonLength = 200;

    private const string Ellipsis = "\u2026";

    /// <summary>
    /// Replaces every control character (CR, LF, ESC and the rest) and every line separator or bidirectional control
    /// (U+2028, U+2029, U+061C, U+200E, U+200F, U+202A..U+202E, U+2066..U+2069) with a space, so a message can neither forge a log
    /// entry nor reorder the text a user reads.
    /// </summary>
    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Any(IsUnsafe))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            builder.Append(IsUnsafe(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Truncates to at most <paramref name="maxLength"/> characters, never inside a surrogate pair, and marks the cut with a final
    /// "…" (U+2026) that counts toward the limit; then sanitizes: for client text that is logged.
    /// </summary>
    public static string Sanitize(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        if (text.Length > maxLength)
        {
            int keep = maxLength - 1;
            if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
            {
                keep--;
            }

            text = text[..keep] + Ellipsis;
        }

        return Sanitize(text);
    }

    private static bool IsUnsafe(char c) =>
        char.IsControl(c)
        || c is '\u2028' or '\u2029' or '\u061C' or '\u200E' or '\u200F'
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';
}
