using System.Text.Json;
using GapActivator.Application.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Ipc;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Service.Ipc;

/// <summary>Turns an exception of a request into the error the client sees. An unknown failure never leaks its message.</summary>
internal static class IpcErrorMapper
{
    private const string UnexpectedMessage = "An unexpected error occurred. See the service log.";

    public static IpcError Map(Exception exception, string type, bool stopping)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            CommandRejectedException rejected => new IpcError(rejected.Code, rejected.Message, false),
            DbUpdateConcurrencyException =>
                new IpcError(IpcErrorCodes.Conflict, "The data changed while the request ran. Try again.", true),
            IpcUnknownMessageException unknown => new IpcError(IpcErrorCodes.UnknownMessage, unknown.Message, false),
            JsonException json => new IpcError(
                IpcErrorCodes.Invalid, $"The payload of {type} is not valid: {json.Message}", false),
            IpcInvalidPayloadException or DomainException or ArgumentException =>
                new IpcError(IpcErrorCodes.Invalid, exception.Message, false),
            OperationCanceledException when stopping => new IpcError(
                IpcErrorCodes.ServiceStopping, "GapActivator is stopping. Try again when it is running.", true),
            _ => new IpcError(IpcErrorCodes.Internal, UnexpectedMessage, false),
        };
    }

    /// <summary>Whether <see cref="Map"/> gives the generic <c>internal</c> error, so the caller logs the exception at Error.</summary>
    public static bool IsUnexpected(Exception exception, bool stopping = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Map(exception, string.Empty, stopping).Code == IpcErrorCodes.Internal;
    }
}
