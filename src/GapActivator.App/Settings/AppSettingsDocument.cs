namespace GapActivator.App.Settings;

/// <summary>The on-disk shape of the settings file. Enums are strings so an unknown value resets only its own field.</summary>
internal sealed class AppSettingsDocument
{
    public int SchemaVersion { get; set; }

    public string? Theme { get; set; }

    public string? Language { get; set; }

    public bool? Notifications { get; set; }

    public bool? CheckForUpdates { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    public WindowBounds? Window { get; set; }
}
