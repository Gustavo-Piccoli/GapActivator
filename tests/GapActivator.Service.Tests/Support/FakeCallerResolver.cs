using System.IO.Pipes;
using GapActivator.Application.Messaging;
using GapActivator.Service.Ipc;

namespace GapActivator.Service.Tests.Support;

/// <summary>Resolves every client to a scripted caller: the next queued one, else <see cref="Default"/>.</summary>
internal sealed class FakeCallerResolver : ICallerResolver
{
    private readonly Lock _sync = new();

    public Caller Default { get; set; } = Caller.Operator(@"TEST\operator");

    public Queue<Caller> Next { get; } = new();

    public bool OperatorsGroupEnabled { get; set; } = true;

    /// <summary>When set, <see cref="Resolve"/> throws it instead of resolving.</summary>
    public Exception? Failure { get; set; }

    public Caller Resolve(NamedPipeServerStream pipe)
    {
        lock (_sync)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            return Next.TryDequeue(out Caller? next) ? next : Default;
        }
    }
}
