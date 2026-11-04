using System.IO.Pipes;
using GapActivator.Application.Messaging;

namespace GapActivator.Service.Ipc;

internal interface ICallerResolver
{
    bool OperatorsGroupEnabled { get; }

    /// <summary>Call only after a frame was read. Throws <see cref="UnauthorizedAccessException"/> when the client cannot be identified.</summary>
    Caller Resolve(NamedPipeServerStream pipe);
}
