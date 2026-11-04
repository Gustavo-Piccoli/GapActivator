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
        RunAsync(() => GapActivatorHost.Build(args), reportFatal, ct);

    /// <summary>The seam for tests: <paramref name="build"/> supplies the host.</summary>
    public static async Task<int> RunAsync(Func<IHost> build, Action<Exception> reportFatal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(reportFatal);

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
            return ServiceExitCodes.Fatal;
        }
    }
}
