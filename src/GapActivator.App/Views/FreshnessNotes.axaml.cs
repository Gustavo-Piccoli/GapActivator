using Avalonia.Controls;

namespace GapActivator.App.Views;

/// <summary>The notes a page shows above its data; its data context is any <see cref="ViewModels.IFreshnessNotes"/>.</summary>
public sealed partial class FreshnessNotes : UserControl
{
    public FreshnessNotes() => InitializeComponent();
}
