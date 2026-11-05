namespace GapActivator.App.Shell;

/// <summary>Draws the tray icons in code (a filled circle per state), so the app ships no binary assets.</summary>
internal static class TrayIconImages
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    /// <summary>The RGB color of each state: gray, red, blue, green.</summary>
    public static readonly IReadOnlyDictionary<TrayIconState, uint> Colors = new Dictionary<TrayIconState, uint>
    {
        [TrayIconState.Unreachable] = 0x8A8F98,
        [TrayIconState.Attention] = 0xD93636,
        [TrayIconState.Busy] = 0x2F6FED,
        [TrayIconState.Healthy] = 0x1F9D55,
    };

    /// <summary>Builds an .ico with 16, 20, 24 and 32 px 32-bit DIB images.</summary>
    public static byte[] Create(uint rgb)
    {
        var images = Sizes.Select(size => Dib(size, rgb)).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        var offset = 6 + (16 * images.Length);
        for (var i = 0; i < images.Length; i++)
        {
            writer.Write((byte)Sizes[i]);
            writer.Write((byte)Sizes[i]);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Dib(int size, uint rgb)
    {
        var maskStride = ((size + 31) / 32) * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(2 * size);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        double r = (size / 2.0) - 1;
        double center = size / 2.0;
        int red = (int)((rgb >> 16) & 0xFF);
        int green = (int)((rgb >> 8) & 0xFF);
        int blue = (int)(rgb & 0xFF);
        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - center;
                var dy = y + 0.5 - center;
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                var alpha = Math.Clamp(r - d + 0.5, 0, 1);
                // Windows treats 32-bit icon pixels as straight alpha, so the color is stored unchanged.
                var a = (byte)Math.Round(alpha * 255);
                writer.Write(a == 0 ? (byte)0 : (byte)blue);
                writer.Write(a == 0 ? (byte)0 : (byte)green);
                writer.Write(a == 0 ? (byte)0 : (byte)red);
                writer.Write(a);
            }
        }

        writer.Write(new byte[maskStride * size]);
        writer.Flush();
        return stream.ToArray();
    }
}
