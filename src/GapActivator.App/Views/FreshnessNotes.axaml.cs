using Avalonia;
using Avalonia.Controls;

namespace GapActivator.App.Views;

/// <summary>The notes a page shows above its data; its data context is any <see cref="ViewModels.IFreshnessNotes"/>.</summary>
public sealed partial class FreshnessNotes : UserControl
{
    /// <summary>The gap between two notes; the page sets the spacing of its own column so the notes keep the rhythm they had on it.</summary>
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FreshnessNotes, double>(nameof(Spacing), 12);

    public FreshnessNotes() => InitializeComponent();

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpacingProperty)
        {
            Notes.Spacing = Spacing;
        }
    }
}
