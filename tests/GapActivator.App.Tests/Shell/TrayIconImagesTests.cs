using GapActivator.App.Shell;
using static GapActivator.App.Shell.ShellNativeMethods;

namespace GapActivator.App.Tests.Shell;

public sealed class TrayIconImagesTests
{
    [Fact]
    public void Create_IsAnIcoWithFourSizes()
    {
        var ico = TrayIconImages.Create(0x2F6FED);

        (ico[4] | (ico[5] << 8)).ShouldBe(4);
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            var image = IcoImage.SelectImage(ico, size);
            BitConverter.ToInt32(image, 4).ShouldBe(size);
        }

        BitConverter.ToInt32(IcoImage.SelectImage(ico, 100), 4).ShouldBe(32);
    }

    [Fact]
    public void Create_CenterHasTheColorAndCornerIsTransparent()
    {
        var image = IcoImage.SelectImage(TrayIconImages.Create(0x2F6FED), 32);
        const int header = 40;

        // Bottom-up BGRA rows: image row y is stored at file row (31 - y).
        static int Offset(int x, int y) => header + ((((31 - y) * 32) + x) * 4);
        var c = Offset(16, 16);
        image[c].ShouldBe((byte)0xED);
        image[c + 1].ShouldBe((byte)0x6F);
        image[c + 2].ShouldBe((byte)0x2F);
        image[c + 3].ShouldBe((byte)0xFF);
        image[Offset(0, 0) + 3].ShouldBe((byte)0);
    }

    [Fact]
    public void Colors_AreTheFourStateColors()
    {
        TrayIconImages.Colors.Count.ShouldBe(4);
        TrayIconImages.Colors[TrayIconState.Unreachable].ShouldBe(0x8A8F98u);
        TrayIconImages.Colors[TrayIconState.Attention].ShouldBe(0xD93636u);
        TrayIconImages.Colors[TrayIconState.Busy].ShouldBe(0x2F6FEDu);
        TrayIconImages.Colors[TrayIconState.Healthy].ShouldBe(0x1F9D55u);
    }

    [Fact]
    public void Create_EdgePixels_StoreTheFullColorWithStraightAlpha()
    {
        var image = IcoImage.SelectImage(TrayIconImages.Create(0x2F6FED), 32);
        var edge = 0;
        for (var i = 40; i + 3 < 40 + (32 * 32 * 4); i += 4)
        {
            var alpha = image[i + 3];
            if (alpha is > 0 and < 255)
            {
                edge++;
                image[i].ShouldBe((byte)0xED);
                image[i + 1].ShouldBe((byte)0x6F);
                image[i + 2].ShouldBe((byte)0x2F);
            }
            else if (alpha == 0)
            {
                image[i].ShouldBe((byte)0);
                image[i + 1].ShouldBe((byte)0);
                image[i + 2].ShouldBe((byte)0);
            }
        }

        edge.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Create_EachSize_LoadsAsAnIcon(int size)
    {
        var image = IcoImage.SelectImage(TrayIconImages.Create(0x1F9D55), size);

        var icon = CreateIconFromResourceEx(image, (uint)image.Length, true, 0x00030000, size, size, 0);

        icon.ShouldNotBe(0);
        DestroyIcon(icon);
    }
}
