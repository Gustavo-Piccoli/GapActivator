using System.IO.Pipes;
using GapActivator.Application.Messaging;
using GapActivator.Service.Ipc;

namespace GapActivator.App.Tests.EndToEnd;

/// <summary>Resolves every client to a scripted caller: the next queued one, else <see cref="Default"/>.</summary>
internal sealed class TestCallerResolver : ICallerResolver
{
    private readonly Lock _sync = new();

    public Caller Default { get; set; } = Caller.Operator(@"TEST\operator");

    public Queue<Caller> Next { get; } = new();

    public bool OperatorsGroupEnabled => true;

    public Caller Resolve(NamedPipeServerStream pipe)
    {
        lock (_sync)
        {
            return Next.TryDequeue(out Caller? next) ? next : Default;
        }
    }
}
