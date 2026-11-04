using GapActivator.Ipc;
using Microsoft.Extensions.Configuration;

namespace GapActivator.Service.Ipc;

/// <summary>The limits and timings of the pipe server.</summary>
public sealed record IpcOptions
{
    public const string PipeNameKey = "GapActivator:Ipc:PipeName";

    private const int MaxPipeNameLength = 200;

    public string PipeName { get; init; } = IpcProtocol.DefaultPipeName;

    public int MaxConnections { get; init; } = 16;

    public int MaxRequestsInFlight { get; init; } = 8;

    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StateChangedInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int PushQueueCapacity { get; init; } = 256;

    /// <summary>The clock of every IPC timeout; it is never the application's clock.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <exception cref="InvalidOperationException">The configured pipe name is empty, contains a backslash or is too long.</exception>
    public static IpcOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? raw = configuration[PipeNameKey];
        if (raw is null)
        {
            return new IpcOptions();
        }

        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('\\', StringComparison.Ordinal) || raw.Length > MaxPipeNameLength)
        {
            throw new InvalidOperationException(
                $"Invalid value for {PipeNameKey}: a pipe name must not be empty, must not contain '\\' and must have at most {MaxPipeNameLength} characters.");
        }

        return new IpcOptions { PipeName = raw };
    }
}
