using Avalonia;
using GapActivator.App.Settings;

namespace GapActivator.App.Services;

/// <summary>Decides whether the saved window bounds are still usable on the screens that exist now.</summary>
internal static class WindowPlacement
{
    public const int MinWidth = 640;
    public const int MinHeight = 480;
    public const int DefaultWidth = 1100;
    public const int DefaultHeight = 720;

    private const int StripHeight = 40;
    private const int MinOverlapWidth = 100;

    /// <summary>
    /// The saved bounds when they are at least 640 × 480 and the window's top 40 px strip overlaps a working area by at least
    /// 100 × 40 px (so the title bar can still be grabbed); otherwise null, and the window opens centered.
    /// </summary>
    public static WindowBounds? Choose(WindowBounds? saved, IReadOnlyList<PixelRect> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (saved is null || saved.Width < MinWidth || saved.Height < MinHeight)
        {
            return null;
        }

        var strip = new PixelRect(saved.X, saved.Y, saved.Width, StripHeight);
        foreach (PixelRect area in workingAreas)
        {
            PixelRect overlap = strip.Intersect(area);
            if (overlap.Width >= MinOverlapWidth && overlap.Height >= StripHeight)
            {
                return saved;
            }
        }

        return null;
    }
}
