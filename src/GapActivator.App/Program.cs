using Avalonia;
using Avalonia.Controls;
using GapActivator.App.Composition;
using GapActivator.App.Settings;
using GapActivator.App.Shell;

namespace GapActivator.App;

internal static class Program
{
    private const int SignalAttempts = 10;

    private static readonly TimeSpan SignalDelay = TimeSpan.FromMilliseconds(200);

    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SignalRunningInstance();
            return 0;
        }

        var options = StartupOptions.Parse(args);
        var services = AppComposition.Build(AppPaths.Default, options);

        App.UseServices(services);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
    // The first instance may hold the mutex before its shell window exists, so retry briefly.
    private static void SignalRunningInstance()
    {
        for (var i = 0; i < SignalAttempts; i++)
        {
            if (SingleInstance.SignalExisting())
            {
                return;
            }

            Thread.Sleep(SignalDelay);
        }
    }
}
