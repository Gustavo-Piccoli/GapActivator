namespace GapActivator.Service.Ipc;

/// <summary>The request names a message the router does not serve, which includes every internal command.</summary>
internal sealed class IpcUnknownMessageException(string type) : Exception($"Unknown message type '{NotificationText.Sanitize(type, NotificationText.MaxReasonLength)}'.");

/// <summary>The payload parsed but a value in it cannot be mapped to the Application.</summary>
internal sealed class IpcInvalidPayloadException(string message) : Exception(message);
