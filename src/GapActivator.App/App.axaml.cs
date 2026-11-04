using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            // The window is not shown yet; later tasks resolve the shell from here.
            _ = s_services ?? throw new InvalidOperationException("Services were not composed.");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
