using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A clipboard that records what was copied, or fails when told to.</summary>
internal sealed class FakeClipboardService : IClipboardService
{
    public List<string> Copied { get; } = [];

    public Exception? Failure { get; set; }

    public Task SetTextAsync(string text)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Copied.Add(text);
        return Task.CompletedTask;
    }
}
