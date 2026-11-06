using GapActivator.App.Composition;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.App.Tests.Composition;

public sealed class AppCompositionTests
{
    [Fact]
    public async Task Build_RegistersTheCultureApplier_SoAComposerCanReplaceIt()
    {
        using var localAppData = new TempDirectory();
        await using ServiceProvider provider = AppComposition.Build(new AppPaths(localAppData.Path), new StartupOptions(true));

        provider.GetRequiredService<ICultureApplier>().ShouldBeOfType<ProcessCultureApplier>();
    }
}
