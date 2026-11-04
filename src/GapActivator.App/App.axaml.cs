using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GapActivator.App.Composition;
using GapActivator.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.App;

public sealed class App : Avalonia.Application
{
    private static IServiceProvider? s_services;

    /// <summary>Hands the composed services to the app; Avalonia creates the instance through its parameterless constructor.</summary>
    internal static void UseServices(IServiceProvider services) => s_services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            var services = s_services ?? throw new InvalidOperationException("Services were not composed.");
            var uiLogger = services.GetRequiredService<ILogger<App>>();
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.UnhandledUiException(uiLogger, e.Exception);
                e.Handled = true;
            };

            services.GetRequiredService<ThemeApplier>().Start();

            // The window is not shown yet; later tasks resolve the shell from here.
        }

        base.OnFrameworkInitializationCompleted();
    }
}
