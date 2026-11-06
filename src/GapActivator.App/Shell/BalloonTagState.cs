namespace GapActivator.App.Shell;

/// <summary>
/// The tag a balloon click reports. The shell gives no way to tell which of several overlapping balloons was clicked, so the state
/// counts the balloons that are live and a click reports the tag only when exactly one is.
/// </summary>
internal sealed class BalloonTagState
{
    private string? _tag;
    private int _live;

    /// <summary>A balloon was shown (the shell accepted it). With another still live, the click target is ambiguous and the tag is dropped.</summary>
    public void Shown(string? tag)
    {
        _live++;
        _tag = _live == 1 ? tag : null;
    }

    /// <summary>A balloon timed out or was hidden. The tag of a balloon that outlives an ended one is not known.</summary>
    public void Ended()
    {
        if (_live > 0)
        {
            _live--;
        }

        if (_live == 0)
        {
            _tag = null;
        }
    }

    /// <summary>The balloon was clicked: returns the tag to report (null when unknown or ambiguous) and clears the state.</summary>
    public string? Clicked()
    {
        var tag = _live == 1 ? _tag : null;
        Reset();
        return tag;
    }

    /// <summary>Forgets every balloon, for when the shell lost them (the taskbar was recreated).</summary>
    public void Reset()
    {
        _tag = null;
        _live = 0;
    }
}
