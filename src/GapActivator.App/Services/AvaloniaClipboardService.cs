using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace GapActivator.App.Services;

/// <summary>The clipboard of the main window.</summary>
internal sealed class AvaloniaClipboardService(Func<TopLevel?> owner) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        IClipboard clipboard = owner()?.Clipboard ?? throw new InvalidOperationException("The clipboard is not available.");
        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }
}
