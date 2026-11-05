using Avalonia;
using GapActivator.App.Services;
using GapActivator.App.Settings;

namespace GapActivator.App.Tests.Services;

public class WindowPlacementTests
{
    private static readonly IReadOnlyList<PixelRect> Screen = [new PixelRect(0, 0, 1920, 1080)];

    [Fact]
    public void Choose_BoundsInsideAScreen_AreUsed()
    {
        var saved = new WindowBounds(100, 80, 1100, 720, false);

        WindowPlacement.Choose(saved, Screen).ShouldBe(saved);
    }

    [Fact]
    public void Choose_NothingSaved_GivesNull() => WindowPlacement.Choose(null, Screen).ShouldBeNull();

    [Fact]
    public void Choose_BoundsOffEveryScreen_AreIgnored()
    {
        var saved = new WindowBounds(5000, 80, 1100, 720, false);

        WindowPlacement.Choose(saved, Screen).ShouldBeNull();
    }

    [Fact]
    public void Choose_TitleBarAboveTheScreen_IsIgnored()
    {
        var saved = new WindowBounds(100, -300, 1100, 720, false);

        WindowPlacement.Choose(saved, Screen).ShouldBeNull();
    }

    [Fact]
    public void Choose_OnlyASliverOverlaps_IsIgnored()
    {
        var saved = new WindowBounds(1850, 80, 1100, 720, false);

        WindowPlacement.Choose(saved, Screen).ShouldBeNull();
    }

    [Fact]
    public void Choose_BoundsOnASecondScreen_AreUsed()
    {
        IReadOnlyList<PixelRect> screens = [new PixelRect(0, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1080)];
        var saved = new WindowBounds(2100, 80, 1100, 720, false);

        WindowPlacement.Choose(saved, screens).ShouldBe(saved);
    }

    [Fact]
    public void Choose_NoScreens_GivesNull() =>
        WindowPlacement.Choose(new WindowBounds(100, 80, 1100, 720, false), []).ShouldBeNull();

    [Theory]
    [InlineData(639, 480)]
    [InlineData(640, 479)]
    public void Choose_TooSmall_AreIgnored(int width, int height)
    {
        var saved = new WindowBounds(100, 80, width, height, false);

        WindowPlacement.Choose(saved, Screen).ShouldBeNull();
    }

    [Fact]
    public void Choose_ExactMinimumSize_IsUsed()
    {
        var saved = new WindowBounds(100, 80, 640, 480, false);

        WindowPlacement.Choose(saved, Screen).ShouldBe(saved);
    }
}
