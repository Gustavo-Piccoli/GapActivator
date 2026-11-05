namespace GapActivator.App.Services;

/// <summary>Puts text on the system clipboard; a failure (no window, clipboard busy) is thrown to the caller.</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
