using GapActivator.App.Shell;

namespace GapActivator.App.Tests.Fakes;

/// <summary>A tray icon that records what it is told and lets a test raise its events and script the menu answer.</summary>
internal sealed class FakeTrayIcon : ITrayIcon
{
    public List<(TrayIconState State, string Tooltip)> States { get; } = [];

    public List<(string Title, string Message, string? Tag, BalloonSeverity Severity)> Balloons { get; } = [];

    public List<IReadOnlyList<TrayMenuItem>> Menus { get; } = [];

    /// <summary>The item Id <see cref="ShowMenu"/> returns.</summary>
    public string? MenuChoice { get; set; }

    /// <summary>What <see cref="ShowBalloon"/> returns; false simulates an icon that is not in the notification area yet.</summary>
    public bool BalloonResult { get; set; } = true;

    public bool Disposed { get; private set; }

    public TrayIconState LastState => States[^1].State;

    public string LastTooltip => States[^1].Tooltip;

    public event EventHandler? OpenRequested;

    public event EventHandler? MenuRequested;

    public event EventHandler<string?>? BalloonClicked;

    public void SetState(TrayIconState state, string tooltip) => States.Add((state, tooltip));

    public bool ShowBalloon(string title, string message, string? tag, BalloonSeverity severity)
    {
        if (BalloonResult)
        {
            Balloons.Add((title, message, tag, severity));
        }

        return BalloonResult;
    }

    public string? ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        Menus.Add(items);
        return MenuChoice;
    }

    public void Dispose() => Disposed = true;

    public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseMenu() => MenuRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseBalloonClicked(string? tag) => BalloonClicked?.Invoke(this, tag);
}
