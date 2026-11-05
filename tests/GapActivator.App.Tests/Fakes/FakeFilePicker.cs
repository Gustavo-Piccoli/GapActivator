using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A file picker that answers with a settable path and counts how often it was opened.</summary>
internal sealed class FakeFilePicker : IFilePicker
{
    public string? Path { get; set; }

    public int Opened { get; private set; }

    public Task<string?> PickExecutableAsync()
    {
        Opened++;
        return Task.FromResult(Path);
    }
}
