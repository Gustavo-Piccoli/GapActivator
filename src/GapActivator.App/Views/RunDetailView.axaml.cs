using Avalonia;
using Avalonia.Controls;

namespace GapActivator.App.Views;

public sealed partial class RunDetailView : UserControl
{
    private ScrollViewer? _scroller;
    private bool _following = true;
    private double _offsetY;

    public RunDetailView()
    {
        InitializeComponent();
        OutputList.TemplateApplied += (_, e) =>
        {
            _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (_scroller is not null)
            {
                _scroller.ScrollChanged += OnScrollChanged;
            }
        };
    }

    // Tail following: the list stays at the last line after a refresh while the user has not scrolled up; once they have, their
    // position is kept. A pure offset change is the user's (or ours, to the end); an extent change is a refresh swapping the lines.
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroller is not { } scroller)
        {
            return;
        }

        if (e.ExtentDelta.Y == 0 && e.OffsetDelta.Y != 0)
        {
            _offsetY = scroller.Offset.Y;
            _following = scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 1;
        }
        else if (e.ExtentDelta.Y != 0)
        {
            scroller.Offset = new Vector(scroller.Offset.X, _following ? double.MaxValue : _offsetY);
        }
    }
}
