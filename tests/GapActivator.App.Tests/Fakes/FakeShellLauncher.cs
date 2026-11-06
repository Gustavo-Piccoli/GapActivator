using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

/// <summary>Records the folders and URLs the app asked the shell to open; can be told to fail.</summary>
internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Folders { get; } = [];

    public List<Uri> Urls { get; } = [];

    public Exception? Failure { get; set; }

    public void OpenFolder(string path)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Folders.Add(path);
    }

    public void OpenUrl(Uri url)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Urls.Add(url);
    }
}
