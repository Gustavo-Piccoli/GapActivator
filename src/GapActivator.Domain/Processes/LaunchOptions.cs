using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>What to launch and with which arguments, working directory and environment.</summary>
public sealed record LaunchOptions
{
    public const int MaxArgumentsLength = 8_191;

    public LaunchOptions(
        ExecutablePath executable,
        string arguments,
        string? workingDirectory,
        EnvironmentVariables environment)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        if (arguments.Length > MaxArgumentsLength)
        {
            throw new DomainException($"Arguments must not exceed {MaxArgumentsLength} characters.");
        }

        if (workingDirectory is not null && !Path.IsPathFullyQualified(workingDirectory))
        {
            throw new DomainException("Working directory must be fully qualified.");
        }

        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Environment = environment;
    }

    public ExecutablePath Executable { get; }

    public string Arguments { get; }

    public string? WorkingDirectory { get; }

    /// <summary>The configured working directory, or the executable's folder when none is set.</summary>
    public string EffectiveWorkingDirectory => WorkingDirectory ?? Path.GetDirectoryName(Executable.Value)!;

    public EnvironmentVariables Environment { get; }
}
