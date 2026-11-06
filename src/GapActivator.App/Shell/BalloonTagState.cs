namespace GapActivator.App.Shell;

/// <summary>
/// The tag a balloon click reports. The shell gives no way to tell which of two overlapping balloons was clicked, so the tag is
/// only reported when exactly one balloon has been live since the last clear; otherwise a click reports no tag.
/// </summary>
internal sealed class BalloonTagState
{
    private string? _tag;
    private bool _live;

    /// <summary>A balloon was shown. If another is still live, the click target is ambiguous and the tag is dropped.</summary>
    public void Shown(string? tag)
    {
        _tag = _live ? null : tag;
        _live = true;
    }

    /// <summary>The balloon timed out or was hidden.</summary>
    public void Ended()
    {
        _tag = null;
        _live = false;
    }

    /// <summary>The balloon was clicked: returns the tag to report (null when unknown or ambiguous) and clears the state.</summary>
    public string? Clicked()
    {
        var tag = _tag;
        Ended();
        return tag;
    }
}
