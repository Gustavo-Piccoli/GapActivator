using System.Reflection;

namespace GapActivator.Ipc;

public sealed record GapActivatorClientOptions
{
    public string PipeName { get; init; } = IpcProtocol.DefaultPipeName;

    /// <summary>Bounds the connect, the server verification and the Hello together.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public string ServiceName { get; init; } = IpcProtocol.ServiceName;

    public string ClientVersion { get; init; } = DefaultClientVersion;

    /// <summary>Test seam; the default asks the Service Control Manager.</summary>
    internal Func<string, ServiceLookup>? LookupService { get; init; }

    private static string DefaultClientVersion { get; } =
        typeof(GapActivatorClientOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}

/// <summary>A request the server rejected, or a failure the client reports with the same shape.</summary>
public sealed class GapActivatorRequestException(string code, string message, bool retryable) : Exception(message)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;
}

public sealed class DisconnectedEventArgs(Exception? error) : EventArgs
{
    /// <summary>Why the connection ended; null for an orderly end (end of stream, or disposal).</summary>
    public Exception? Error { get; } = error;
}
