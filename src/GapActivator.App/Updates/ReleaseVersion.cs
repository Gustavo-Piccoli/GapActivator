using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GapActivator.App.Updates;

/// <summary>Parses release tags such as <c>v1.2.3-beta.1</c> into comparable <c>major.minor.build</c> versions.</summary>
public static class ReleaseVersion
{
    public static bool TryParse(string text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        var span = text.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        var cut = span.IndexOfAny('-', '+');
        if (cut >= 0)
        {
            span = span[..cut];
        }

        Span<int> parts = stackalloc int[3];
        var count = 0;
        foreach (var range in span.Split('.'))
        {
            if (count == 3 || !int.TryParse(span[range], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return false;
            }

            parts[count++] = n;
        }

        if (count < 2)
        {
            return false;
        }

        version = new Version(parts[0], parts[1], parts[2]);
        return true;
    }

    public static Version Normalize(Version v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
