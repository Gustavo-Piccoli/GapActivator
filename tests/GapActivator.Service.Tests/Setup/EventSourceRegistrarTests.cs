using System.Diagnostics;
using GapActivator.Infrastructure.Logging;
using GapActivator.Service.Setup;
using GapActivator.Service.Tests.Support;

namespace GapActivator.Service.Tests.Setup;

public sealed class EventSourceRegistrarTests
{
    private sealed class FakeBackend : IEventSourceBackend
    {
        public bool Exists { get; set; }

        public List<string> Calls { get; } = [];

        public bool SourceExists(string source) => Exists;

        public void Create(string source, string log)
        {
            Calls.Add($"create:{source}:{log}");
            Exists = true;
        }

        public void Delete(string source)
        {
            Calls.Add("delete:" + source);
            Exists = false;
        }
    }

    [Fact]
    public void Register_CreatesTheSourceInTheApplicationLog_OnlyOnce()
    {
        var backend = new FakeBackend();
        var registrar = new EventSourceRegistrar(backend);

        registrar.Register();
        registrar.Register();

        backend.Calls.ShouldBe(["create:GapActivator:Application"]);
        GapActivatorLogging.EventSource.ShouldBe("GapActivator");
    }

    [Fact]
    public void Unregister_DeletesTheSource_OnlyWhenItExists()
    {
        var backend = new FakeBackend { Exists = true };
        var registrar = new EventSourceRegistrar(backend);

        registrar.Unregister();
        registrar.Unregister();

        backend.Calls.ShouldBe(["delete:GapActivator"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RegisterThenUnregister_RealEventLog_IsIdempotent()
    {
        Elevation.SkipUnlessElevated();
        bool existedBefore = EventLog.SourceExists(GapActivatorLogging.EventSource);
        var registrar = new EventSourceRegistrar();
        try
        {
            registrar.Register();
            registrar.Register();
            EventLog.SourceExists(GapActivatorLogging.EventSource).ShouldBeTrue();

            registrar.Unregister();
            registrar.Unregister();
            EventLog.SourceExists(GapActivatorLogging.EventSource).ShouldBeFalse();
        }
        finally
        {
            if (existedBefore)
            {
                registrar.Register();
            }
            else
            {
                registrar.Unregister();
            }
        }
    }
}
