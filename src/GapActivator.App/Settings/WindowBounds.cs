namespace GapActivator.App.Settings;

/// <summary>The main window's last position and size, in device-independent pixels.</summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);
