namespace GapActivator.App.Shell;

/// <summary>An entry of the tray context menu; <see cref="Children"/> makes it a submenu.</summary>
public sealed record TrayMenuItem(string Id, string Text, IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new("-", string.Empty);
}
