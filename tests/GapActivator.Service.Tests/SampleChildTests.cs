using System.Diagnostics;
using System.Text;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Processes;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;

namespace GapActivator.Service.Tests;

[Trait("Category", "Integration")]
public sealed class SampleChildTests : IAsyncLifetime
{
    private static readonly TimeSpan OutputWait = TimeSpan.FromSeconds(10);

    private readonly TempDataRoot _root = new();
    private readonly LogRecorder _logs = new();
    private IRunningProcess? _launched;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_launched is not null)
        {
            await _launched.DisposeAsync();
        }

        await _root.DisposeAsync();
    }

    [Fact]
    public async Task SampleChild_OnItsOwn_RunsAndExitsZero()
    {
        var startInfo = new ProcessStartInfo(ChildPaths.SampleChild, "1")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment.Remove("GAPACTIVATOR_PARENT_PID");

        using Process process = Process.Start(startInfo).ShouldNotBeNull();
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(Ct);
            await process.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

            process.ExitCode.ShouldBe(0);
            output.ShouldContain("Running on its own.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    // BI-057, BI-060
    [Fact]
    public async Task SampleChild_UnderTheLauncher_StopsCleanlyOnCancel()
    {
        var paths = new GapActivatorPaths(_root.Path);
        paths.EnsureDirectories();
        var launcher = new WindowsProcessLauncher(new InfrastructureOptions(paths), new RecordingLogger<WindowsProcessLauncher>(_logs));
        var request = new LaunchRequest(new RunId(Guid.CreateVersion7()), ChildPaths.SampleChild, "60", AppContext.BaseDirectory, []);
        _launched = await launcher.LaunchAsync(request, Ct);
        string outputFile = paths.RunOutputFile(request.RunId);

        await WaitForOutputAsync(outputFile, "Working... 1/60");
        _launched.SignalCancel();

        (await _launched.Exited.WaitAsync(TimeSpan.FromSeconds(3), Ct)).ShouldBe(0);
        string output = ReadOutput(outputFile);
        output.ShouldContain($"Started by GapActivator (PID {Environment.ProcessId}).");
        output.ShouldContain("Cancellation requested; stopping cleanly.");
    }

    private static string ReadOutput(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task WaitForOutputAsync(string path, string expected)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            string output = File.Exists(path) ? ReadOutput(path) : "";
            if (output.Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            if (watch.Elapsed > OutputWait)
            {
                throw new TimeoutException($"The output never contained '{expected}'; it was '{output}'.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
        }
    }
}
