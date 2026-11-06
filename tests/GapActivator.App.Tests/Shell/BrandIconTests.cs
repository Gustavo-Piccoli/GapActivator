using Avalonia.Platform;

namespace GapActivator.App.Tests.Shell;

public sealed class BrandIconTests(HeadlessFixture fixture)
{
    private static readonly Uri IconUri = new("avares://GapActivator/Assets/Icons/gapactivator.ico");

    private Task<byte[]> ReadIcon() =>
        fixture.Session.Dispatch(
            () =>
            {
                using var stream = AssetLoader.Open(IconUri);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task EmbeddedIcon_HasTheEightSizes()
    {
        var ico = await ReadIcon();

        BitConverter.ToUInt16(ico, 2).ShouldBe((ushort)1);
        BitConverter.ToUInt16(ico, 4).ShouldBe((ushort)8);
        var sizes = Enumerable.Range(0, 8)
            .Select(i => ico[6 + (16 * i)] == 0 ? 256 : ico[6 + (16 * i)])
            .ToArray();
        sizes.ShouldBe([16, 20, 24, 32, 40, 48, 64, 256]);
    }

    [Fact]
    public async Task EmbeddedIcon_EveryEntryIsAPng()
    {
        var ico = await ReadIcon();

        for (var i = 0; i < 8; i++)
        {
            var offset = (int)BitConverter.ToUInt32(ico, 6 + (16 * i) + 12);
            ico.AsSpan(offset, 4).ToArray().ShouldBe([0x89, 0x50, 0x4E, 0x47]);
        }
    }
}
