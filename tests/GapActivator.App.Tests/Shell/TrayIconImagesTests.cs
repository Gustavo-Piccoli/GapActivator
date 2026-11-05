using GapActivator.App.Shell;

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
}
