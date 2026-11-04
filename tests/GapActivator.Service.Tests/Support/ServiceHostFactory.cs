using GapActivator.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace GapActivator.Service.Tests.Support;

/// <summary>
/// The real host builder on a test data root. The exit-code sink is replaced, so the test process's exit code is never
/// set; the Event Log is switched off, so a test never writes to the machine's Event Log, and so is the console.
/// </summary>
internal static class ServiceHostFactory
{
    public static HostApplicationBuilder CreateBuilder(string dataRoot, List<int> exitCodes, LogCapture? logs = null)
    {
        HostApplicationBuilder builder = GapActivatorHost.CreateBuilder(["--GapActivator:DataRoot=" + dataRoot]);

        builder.Services.Replace(ServiceDescriptor.Singleton(new ExitCodeSink(code =>
        {
            lock (exitCodes)
            {
                exitCodes.Add(code);
            }
        })));

        // The last rule of equal specificity wins, so these switch the Event Log and the console off entirely.
        builder.Logging.AddFilter<SafeEventLogLoggerProvider>(null, LogLevel.None);
        builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.None);

        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        return builder;
    }

    /// <summary>Completes when the host begins to stop; fails after <paramref name="timeout"/>.</summary>
    public static async Task WaitForStoppingAsync(IHost host, TimeSpan timeout, CancellationToken ct)
    {
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using CancellationTokenRegistration registration = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        await stopping.Task.WaitAsync(timeout, ct);
    }
}
