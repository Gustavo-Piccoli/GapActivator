namespace GapActivator.Infrastructure.Processes;

/// <summary>Builds the <c>CreateProcessW</c> application name and command line of a registration.</summary>
internal static class CommandLineBuilder
{
    /// <summary>
    /// Quotes the executable and appends the arguments verbatim. <c>.cmd</c> and <c>.bat</c> files run through
    /// <c>cmd.exe /d /s /c</c>, because <c>CreateProcessW</c> does not officially start batch files.
    /// </summary>
    public static (string ApplicationName, string CommandLine) Build(string executable, string arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        string invocation = arguments.Length == 0 ? $"\"{executable}\"" : $"\"{executable}\" {arguments}";
        if (!IsBatchFile(executable))
        {
            return (executable, invocation);
        }

        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return (cmd, $"\"{cmd}\" /d /s /c \"{invocation}\"");
    }

    public static bool IsBatchFile(string executable)
    {
        string extension = Path.GetExtension(executable);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }
}
