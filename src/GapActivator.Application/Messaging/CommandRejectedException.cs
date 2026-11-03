namespace GapActivator.Application.Messaging;

/// <summary>A command was refused for a business reason identified by a <see cref="RejectionCodes"/> value.</summary>
public sealed class CommandRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
