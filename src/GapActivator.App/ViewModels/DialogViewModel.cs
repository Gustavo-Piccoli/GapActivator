namespace GapActivator.App.ViewModels;

/// <summary>What the modal dialog shows: a title, a message, and up to three buttons.</summary>
public sealed record DialogViewModel(string Title, string Message, string Primary, string? Secondary, string Cancel)
{
    public bool HasSecondary => Secondary is not null;
}
