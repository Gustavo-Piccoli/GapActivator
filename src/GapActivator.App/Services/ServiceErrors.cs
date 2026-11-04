using GapActivator.App.Localization;
using GapActivator.Ipc;

namespace GapActivator.App.Services;

/// <summary>Turns what the service or the connection reports into the text the user reads.</summary>
internal static class ServiceErrors
{
    public static IReadOnlyCollection<string> KnownCodes { get; } =
    [
        IpcErrorCodes.NotFound,
        IpcErrorCodes.Unauthorized,
        IpcErrorCodes.NameTaken,
        IpcErrorCodes.ScheduleInPast,
        IpcErrorCodes.SecretValueRequired,
        IpcErrorCodes.HasActiveRun,
        IpcErrorCodes.RunAlreadyActive,
        IpcErrorCodes.RunNotActive,
        IpcErrorCodes.ProcessBlocked,
        IpcErrorCodes.ProcessDisabled,
        IpcErrorCodes.InstallationPaused,
        IpcErrorCodes.UseDisable,
        IpcErrorCodes.EmailTestFailed,
        IpcErrorCodes.Conflict,
        IpcErrorCodes.Invalid,
        IpcErrorCodes.UnknownMessage,
        IpcErrorCodes.Internal,
        IpcErrorCodes.ServiceStopping,
        IpcErrorCodes.ProtocolMismatch,
        IpcErrorCodes.Busy,
        IpcErrorCodes.Disconnected,
        IpcErrorCodes.ServerNotVerified,
    ];

    public static string Describe(Exception error, Localizer l)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            GapActivatorRequestException request => Describe(request.Code, request.Message, l),
            TimeoutException => Describe(IpcErrorCodes.Disconnected, null, l),
            _ => l.ErrorUnexpected(error.Message),
        };
    }

    /// <summary>The text of an error code. A code the app does not know gives the service message, or the code when there is none.</summary>
    public static string Describe(string code, string? serviceMessage, Localizer l)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(l);
        string? template = l.ErrorTemplate(code);
        return template is null
            ? serviceMessage ?? code
            : string.Format(l.Culture, template, serviceMessage ?? string.Empty);
    }

    public static bool Is(Exception error, string code) =>
        error is GapActivatorRequestException request && request.Code == code;
}
