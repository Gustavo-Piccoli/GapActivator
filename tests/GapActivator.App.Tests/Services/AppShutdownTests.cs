using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Tests.Services;

public class AppShutdownTests
{
    [Fact]
    public void Exit_RunsEveryStepOnceEvenWhenOneThrows()
    {
        var ran = new List<int>();
        var logger = new ListLogger<AppShutdown>();
        var shutdown = new AppShutdown(
            [() => ran.Add(0), () => throw new InvalidOperationException("boom"), () => ran.Add(2)],
            logger);

        shutdown.Exit();
        shutdown.Exit();

        ran.ShouldBe([0, 2]);
        logger.Levels.ShouldContain(LogLevel.Error);
    }

    [Fact]
    public void Exit_RunsTheStepsInOrder()
    {
        var ran = new List<int>();
        var shutdown = new AppShutdown([() => ran.Add(0), () => ran.Add(1), () => ran.Add(2)], new ListLogger<AppShutdown>());

        shutdown.Exit();

        ran.ShouldBe([0, 1, 2]);
    }
}
