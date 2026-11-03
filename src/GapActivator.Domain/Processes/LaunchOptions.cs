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

        var directory = workingDirectory?.Trim();
        if (directory is not null)
        {
            ValidateWorkingDirectory(directory);
        }

        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = directory;
        Environment = environment;
    }

    public ExecutablePath Executable { get; }

    public string Arguments { get; }

    public string? WorkingDirectory { get; }

    /// <summary>The configured working directory, or the executable's folder when none is set.</summary>
    public string EffectiveWorkingDirectory => WorkingDirectory ?? Path.GetDirectoryName(Executable.Value.AsSpan()).ToString();

    public EnvironmentVariables Environment { get; }

    private static void ValidateWorkingDirectory(string directory)
    {
        if (directory.Length > ExecutablePath.MaxLength)
        {
            throw new DomainException($"Working directory must not exceed {ExecutablePath.MaxLength} characters.");
        }

        if (directory.Contains('"')
            || directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || directory.IndexOfAny(ExecutablePath.Wildcards) >= 0)
        {
            throw new DomainException("Working directory contains invalid characters.");
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            throw new DomainException("Working directory must be fully qualified.");
        }
    }
}
