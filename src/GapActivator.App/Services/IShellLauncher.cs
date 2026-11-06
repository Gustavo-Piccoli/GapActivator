namespace GapActivator.App.Services;

/// <summary>Hands a folder or a web page to Windows. Both calls can throw when the shell has no handler.</summary>
public interface IShellLauncher
{
    /// <summary>Opens an existing folder in Explorer; the caller checks that it exists.</summary>
    void OpenFolder(string path);

    /// <summary>Opens an https page in the default browser; anything else is ignored.</summary>
    void OpenUrl(Uri url);
}
