using System.Text;

namespace GapActivator.Service.Ipc;

/// <summary>Cleans the text of a notification before it reaches the log or a client.</summary>
internal static class NotificationText
{
    /// <summary>
    /// Replaces every control character (CR, LF, ESC and the rest) and every line separator or bidirectional control
    /// (U+2028, U+2029, U+200E, U+200F, U+202A..U+202E, U+2066..U+2069) with a space, so a message can neither forge a log
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

    private static bool IsUnsafe(char c) =>
        char.IsControl(c)
        || c is '\u2028' or '\u2029' or '\u200E' or '\u200F'
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';
}
