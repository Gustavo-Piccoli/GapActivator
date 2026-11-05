using GapActivator.App.Services;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A dialog service that answers from a script and records what it was asked.</summary>
internal sealed class ScriptedDialogService : IDialogService
{
    private readonly Queue<DialogChoice> _answers = new();

    public List<(string Title, string Message, string Primary, string? Secondary, string Cancel)> Asked { get; } = [];

    public void Enqueue(params DialogChoice[] answers)
    {
        foreach (DialogChoice answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary, string cancel)
    {
        Asked.Add((title, message, primary, secondary, cancel));
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : DialogChoice.Cancel);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm) =>
        await AskAsync(title, message, confirm, null, "Cancel").ConfigureAwait(false) == DialogChoice.Primary;
}
