using System.Reflection;

namespace GapActivator.Ipc;

/// <summary>Marker used by architecture tests to reference this assembly.</summary>
public static class IpcAssembly
{
    public static Assembly Reference => typeof(IpcAssembly).Assembly;
}

public static class IpcEvents
{
    public const string Notification = "Notification";
    public const string StateChanged = "StateChanged";
}

public static class IpcProtocol
{
    public const int Version = 1;
    public const string DefaultPipeName = "GapActivator";
    public const string ServiceName = "GapActivator";
    public const int MaxPayloadBytes = 16 * 1024 * 1024;

    /// <summary>Whether this build speaks <paramref name="version"/>; the client and the server both call it.</summary>
    public static bool IsSupported(int version) => version == Version;
}

public static class IpcErrorCodes
{
    // The Application rejection codes, with the same strings.
    public const string NotFound = "not-found";
    public const string Unauthorized = "unauthorized";
    public const string NameTaken = "name-taken";
    public const string ScheduleInPast = "schedule-in-past";
    public const string SecretValueRequired = "secret-value-required";
    public const string HasActiveRun = "has-active-run";
    public const string RunAlreadyActive = "run-already-active";
    public const string RunNotActive = "run-not-active";
    public const string ProcessBlocked = "process-blocked";
    public const string ProcessDisabled = "process-disabled";
    public const string InstallationPaused = "installation-paused";
    public const string UseDisable = "use-disable";
    public const string EmailTestFailed = "email-test-failed";

    // Raised by the transport and the dispatcher.
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string UnknownMessage = "unknown-message";
    public const string Internal = "internal";
    public const string ServiceStopping = "service-stopping";
    public const string ProtocolMismatch = "protocol-mismatch";
    public const string Busy = "busy";

    // Raised by the client only.
    public const string Disconnected = "disconnected";
    public const string ServerNotVerified = "server-not-verified";
}
