using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GapActivator.App.Localization;

namespace GapActivator.App.Services;

/// <summary>The native open-file dialog of the main window.</summary>
internal sealed class AvaloniaFilePicker(Localizer l, Func<TopLevel?> owner) : IFilePicker
{
    public async Task<string?> PickExecutableAsync()
    {
        TopLevel? top = owner();
        if (top is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = l.BrowseTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(l.BrowseExecutableFilter) { Patterns = ["*.exe", "*.cmd", "*.bat"] }],
        }).ConfigureAwait(true);
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
