namespace GapActivator.Application.Messaging;

/// <summary>Scoped: the caller of the message being dispatched. Queries read it to mask data.</summary>
public sealed class CallerAccessor
{
    public Caller Current { get; internal set; } = Caller.System;
}
