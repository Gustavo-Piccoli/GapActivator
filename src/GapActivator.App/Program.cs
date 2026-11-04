using Avalonia;
using Avalonia.Controls;
using GapActivator.App.Composition;
using GapActivator.App.Settings;

namespace GapActivator.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);
        var services = AppComposition.Build(AppPaths.Default, options);

        App.UseServices(services);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
