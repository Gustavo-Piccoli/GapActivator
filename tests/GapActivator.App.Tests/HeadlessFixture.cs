using Avalonia;
using Avalonia.Headless;
using GapActivator.App.Tests;

[assembly: AssemblyFixture(typeof(HeadlessFixture))]

namespace GapActivator.App.Tests;

/// <summary>Owns the single headless Avalonia session shared by every view test.</summary>
public sealed class HeadlessFixture : IAsyncDisposable
{
    public HeadlessFixture() => Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public HeadlessUnitTestSession Session { get; }

    public ValueTask DisposeAsync()
    {
        // Disposing the session joins its dispatcher thread, which does not return on Avalonia 12.1.4 and would keep
        // the test host alive for ever. The thread is a background thread, so give it a moment and let the host exit.
        var disposal = Task.Run(Session.Dispose);
        _ = disposal.Wait(TimeSpan.FromSeconds(2));
        return ValueTask.CompletedTask;
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
