namespace GapActivator.App.Shell;

/// <summary>The notification-area icon, its balloons and its context menu.</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>Sets the icon state and tooltip; the first call adds the icon.</summary>
    void SetState(TrayIconState state, string tooltip);

    /// <summary>Shows a balloon; false when it could not be shown (for example the icon is not in the notification area yet).</summary>
    bool ShowBalloon(string title, string message, string? tag, BalloonSeverity severity);

    /// <summary>Shows the menu, blocks until it closes and returns the chosen item Id (null if dismissed).</summary>
    string? ShowMenu(IReadOnlyList<TrayMenuItem> items);

    event EventHandler? OpenRequested;

    event EventHandler? MenuRequested;

    /// <summary>Raised with the tag of the clicked balloon.</summary>
    event EventHandler<string?>? BalloonClicked;
}
