using System.Diagnostics;

namespace GapActivator.App.Services;

public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Process.Start(CreateOpenFolderStartInfo(path))?.Dispose();
    }

    /// <summary>Explorer by its rooted path in the Windows folder: a bare name would be searched for in the current and application folders first.</summary>
    internal static ProcessStartInfo CreateOpenFolderStartInfo(string path) =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{path}\"") { UseShellExecute = true };

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }
}
