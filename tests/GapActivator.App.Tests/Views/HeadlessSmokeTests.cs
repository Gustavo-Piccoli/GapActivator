using Avalonia.Media;
using Avalonia.Styling;

namespace GapActivator.App.Tests.Views;

public class HeadlessSmokeTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task App_Tokens_ResolveGaGroundBrushInBothThemes(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        var brush = await fixture.Session.Dispatch(
            () =>
            {
                var app = Avalonia.Application.Current.ShouldNotBeNull();
                app.TryGetResource("GaGroundBrush", theme, out var found).ShouldBeTrue();
                return found as ISolidColorBrush;
            },
            TestContext.Current.CancellationToken);

        brush.ShouldNotBeNull();
    }
}
