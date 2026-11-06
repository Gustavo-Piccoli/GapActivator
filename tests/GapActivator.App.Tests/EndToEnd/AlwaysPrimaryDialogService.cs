using GapActivator.App.Services;

namespace GapActivator.App.Tests.EndToEnd;

/// <summary>Answers every question with the primary button, as a user who agrees with everything would.</summary>
internal sealed class AlwaysPrimaryDialogService : IDialogService
{
    public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary, string cancel) =>
        Task.FromResult(DialogChoice.Primary);

    public Task<bool> ConfirmAsync(string title, string message, string confirm) => Task.FromResult(true);
}
