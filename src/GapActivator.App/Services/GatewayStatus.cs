using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Services;

public enum ConnectionState
{
    Connecting,
    Connected,
    ServiceStopped,
    ServiceNotInstalled,
    Refused,
}

/// <summary>What the app knows about its connection to the service.</summary>
public sealed record GatewayStatus(
    ConnectionState State,
    bool ServerVerified,
    CallerDto? Caller,
    OperatorsGroupMode? OperatorsMode,
    string? ServiceVersion,
    string? ErrorCode)
{
    public static GatewayStatus Initial { get; } = new(ConnectionState.Connecting, false, null, null, null, null);

    public bool IsConnected => State == ConnectionState.Connected;

    public bool CanChange => IsConnected && Caller?.CanChange == true;
}
