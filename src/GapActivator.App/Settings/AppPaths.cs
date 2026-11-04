namespace GapActivator.App.Settings;

/// <summary>The app's own files, under the per-user local application data folder.</summary>
public sealed record AppPaths(string Root)
{
    public string SettingsFile => Path.Combine(Root, "app-settings.json");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GapActivator"));
}
