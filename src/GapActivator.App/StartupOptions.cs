namespace GapActivator.App;

/// <summary>Command-line options of the desktop app.</summary>
public sealed record StartupOptions(bool Minimized)
{
    public static StartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var minimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        return new StartupOptions(minimized);
    }
}
