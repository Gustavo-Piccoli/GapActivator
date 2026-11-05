namespace GapActivator.App.Shell;

/// <summary>Reads the image directory of an .ico file.</summary>
public static class IcoImage
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;

    /// <summary>Returns the bytes of the entry whose width is the smallest one &gt;= <paramref name="size"/>, or else the largest.</summary>
    /// <exception cref="InvalidDataException">The data is not a valid icon file.</exception>
    public static byte[] SelectImage(byte[] ico, int size)
    {
        ArgumentNullException.ThrowIfNull(ico);
        if (ico.Length < HeaderSize || ico[0] != 0 || ico[1] != 0 || ico[2] != 1 || ico[3] != 0)
        {
            throw new InvalidDataException("Not an ICO file.");
        }

        int count = ico[4] | (ico[5] << 8);
        if (count == 0 || ico.Length < HeaderSize + (count * EntrySize))
        {
            throw new InvalidDataException("Invalid ICO directory.");
        }

        int bestWidth = -1;
        int bestOffset = 0;
        int bestLength = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = HeaderSize + (i * EntrySize);
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int length = BitConverter.ToInt32(ico, entry + 8);
            int offset = BitConverter.ToInt32(ico, entry + 12);
            if (length <= 0 || offset < 0 || offset > ico.Length - length)
            {
                throw new InvalidDataException("ICO entry out of range.");
            }

            if (IsBetter(width, bestWidth, size))
            {
                bestWidth = width;
                bestOffset = offset;
                bestLength = length;
            }
        }

        return ico.AsSpan(bestOffset, bestLength).ToArray();
    }

    private static bool IsBetter(int width, int bestWidth, int size)
    {
        if (bestWidth < 0)
        {
            return true;
        }

        if (width >= size)
        {
            return bestWidth < size || width < bestWidth;
        }

        return bestWidth < size && width > bestWidth;
    }
}
