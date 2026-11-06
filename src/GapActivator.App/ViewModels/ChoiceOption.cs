using CommunityToolkit.Mvvm.ComponentModel;

namespace GapActivator.App.ViewModels;

/// <summary>
/// One entry of an enum drop-down. The object lives as long as the page, so the selection survives a language switch: only
/// <see cref="Text"/> changes, in place.
/// </summary>
public sealed partial class ChoiceOption(object value, string text) : ObservableObject
{
    public object Value { get; } = value;

    [ObservableProperty]
    public partial string Text { get; set; } = text;
}
