using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GapActivator.Service;

/// <summary>
/// The service's entry point. A failure to build or run the host (for example an invalid data root, which is checked
/// while the builder is composed, before any GapActivator logger exists) is reported and ends with
/// <see cref="ServiceExitCodes.Fatal"/>, instead of an unhandled-exception crash. Once the host has run, the exit
/// code is the one the host set (normal, fatal or restart).
/// </summary>
internal static class ServiceEntry
{
    public static Task<int> RunAsync(string[] args, Action<Exception> reportFatal, CancellationToken ct = default) =>
        RunAsync(() => GapActivatorHost.Build(args), reportFatal, Console.Error.WriteLine, ct);

    public static Task<int> RunAsync(
        Func<IHost> build, Action<Exception> reportFatal, CancellationToken ct = default) =>
        RunAsync(build, reportFatal, Console.Error.WriteLine, ct);

    /// <summary>
    /// The seam for tests: <paramref name="build"/> supplies the host and <paramref name="writeError"/> (standard error
    /// by default) receives the one-line fatal message, so a console run is never silent when no event source exists.
    /// </summary>
    public static async Task<int> RunAsync(
        Func<IHost> build, Action<Exception> reportFatal, Action<string> writeError, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(reportFatal);
        ArgumentNullException.ThrowIfNull(writeError);

        try
        {
            IHost host = build();
            ExitCodeSink exitCode = host.Services.GetRequiredService<ExitCodeSink>();

            // Disposes the host when it has stopped.
            await host.RunAsync(ct);
            return exitCode.Last ?? ServiceExitCodes.Normal;
        }
        catch (Exception ex)
        {
            reportFatal(ex);
            WriteStderrLine(writeError, ex);
            return ServiceExitCodes.Fatal;
        }
    }

    // Best effort: a broken or closed standard error must not replace the fatal exit code.
    private static void WriteStderrLine(Action<string> writeError, Exception exception)
    {
        try
        {
            writeError($"GapActivator failed to start: {exception.Message}");
        }
        catch (Exception)
        {
            // Nothing more can be done.
        }
    }
}
