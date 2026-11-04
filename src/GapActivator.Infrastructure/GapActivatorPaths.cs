using System.Globalization;
using GapActivator.Domain.Runs;

namespace GapActivator.Infrastructure;

/// <summary>
/// The fixed file layout under the data root (spec section 9): the database, the service logs and the run output files.
/// </summary>
public sealed record GapActivatorPaths
{
    /// <summary>Configuration key that overrides the data root (env var <c>GapActivator__DataRoot</c>).</summary>
    public const string DataRootKey = "GapActivator:DataRoot";

    /// <param name="dataRoot">A fully qualified folder; a relative path in a service would resolve under System32.</param>
    public GapActivatorPaths(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException($"The data root '{dataRoot}' must be a fully qualified path.", nameof(dataRoot));
        }

        DataRoot = dataRoot;
    }

    /// <summary><c>%ProgramData%\GapActivator</c>.</summary>
    public static GapActivatorPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GapActivator"));

    public string DataRoot { get; }

    public string DatabaseFile => Path.Combine(DataRoot, "gapactivator.db");

    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    public string RunLogsDirectory => Path.Combine(LogsDirectory, "runs");

    /// <summary>The output file of one run, named by the run id in lower-case <c>D</c> format.</summary>
    public string RunOutputFile(RunId runId) =>
        Path.Combine(RunLogsDirectory, runId.Value.ToString("D", CultureInfo.InvariantCulture) + ".log");

    /// <summary>The service log of one day, dated by the machine's local date.</summary>
    public string ServiceLogFile(DateOnly localDay) =>
        Path.Combine(LogsDirectory, "gapactivator-" + localDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>Creates the data root, the logs folder and the run logs folder when they are missing. Sets no ACLs.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(RunLogsDirectory);
    }
}
