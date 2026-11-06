using System.Diagnostics;
using GapActivator.Infrastructure.Logging;

namespace GapActivator.Service.Setup;

/// <summary>The event log calls, behind a seam so the idempotence is testable without elevation.</summary>
internal interface IEventSourceBackend
{
    bool SourceExists(string source);

    void Create(string source, string log);

    void Delete(string source);
}

internal sealed class SystemEventSourceBackend : IEventSourceBackend
{
    public bool SourceExists(string source) => EventLog.SourceExists(source);

    public void Create(string source, string log) => EventLog.CreateEventSource(source, log);

    public void Delete(string source) => EventLog.DeleteEventSource(source);
}

/// <summary>Creates and removes the <c>GapActivator</c> event source in the Application log. Both are idempotent.</summary>
internal sealed class EventSourceRegistrar(IEventSourceBackend backend)
{
    public EventSourceRegistrar()
        : this(new SystemEventSourceBackend())
    {
    }

    public void Register()
    {
        if (!backend.SourceExists(GapActivatorLogging.EventSource))
        {
            backend.Create(GapActivatorLogging.EventSource, "Application");
        }
    }

    public void Unregister()
    {
        if (backend.SourceExists(GapActivatorLogging.EventSource))
        {
            backend.Delete(GapActivatorLogging.EventSource);
        }
    }
}
