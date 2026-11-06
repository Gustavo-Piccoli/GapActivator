namespace GapActivator.App.Settings;

/// <summary>Where the service keeps its files by default. Shown in About only; the app never reads them.</summary>
public sealed record ServicePaths(string DataRoot)
{
    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public static ServicePaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GapActivator"));
}
