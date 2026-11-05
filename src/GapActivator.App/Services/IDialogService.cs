namespace GapActivator.App.Services;

public enum DialogChoice
{
    Primary,
    Secondary,
    Cancel,
}

/// <summary>Modal questions to the user. Closing the dialog any way other than a button counts as Cancel.</summary>
public interface IDialogService
{
    /// <summary>A question with a primary answer, an optional secondary answer and a cancel button.</summary>
    Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary, string cancel);

    /// <summary>A yes-or-no question; the cancel button is the localized "Cancel".</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirm);
}
