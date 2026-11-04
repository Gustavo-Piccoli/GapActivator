using System.Diagnostics;
using System.Text;
using GapActivator.Application.Queries;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace GapActivator.Service.Tests;

/// <summary>The built service executable, in console mode, on a seeded data root.</summary>
[Trait("Category", "Integration")]
public sealed class ServiceExeSmokeTests
{
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ServiceExe_ConsoleMode_RunsTheScheduledSample()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        IReadOnlyDictionary<string, ProcessId> processes = await E2EFixture.SeedAsync(root.Path, ct);
        File.Exists(ChildPaths.ServiceExe).ShouldBeTrue();

        var startInfo = new ProcessStartInfo(ChildPaths.ServiceExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--GapActivator:DataRoot=" + root.Path);
        startInfo.ArgumentList.Add($"--GapActivator:Ipc:PipeName=GapActivator.Tests.{Guid.NewGuid():N}");
        var console = new StringBuilder();
        DateOnly startDay = DateOnly.FromDateTime(DateTime.Now);

        using DiagnosticsProcess exe = DiagnosticsProcess.Start(startInfo).ShouldNotBeNull();
        string? cleanupFailure = null;
        try
        {
            exe.OutputDataReceived += (_, e) => Append(console, e.Data);
            exe.ErrorDataReceived += (_, e) => Append(console, e.Data);
            exe.BeginOutputReadLine();
            exe.BeginErrorReadLine();

            await using E2ESeeding seeding = E2ESeeding.Open(root.Path);
            Task<RunRow> succeeded = seeding.WaitForRunAsync(
                processes[E2EFixture.ScheduledName], r => r.State == RunState.Completed, RunWait, ct);
            Task exited = exe.WaitForExitAsync(ct);
            if (await Task.WhenAny(succeeded, exited) == exited)
            {
                await exited;
                Assert.Fail($"The service exited with code {exe.ExitCode} before the run ended. Console:\r\n{Read(console)}");
            }

            RunRow run = await succeeded;
            run.Outcome.ShouldBe(RunOutcome.Succeeded);
            run.Trigger.ShouldBe(RunTrigger.Schedule);
            run.ExitCode.ShouldBe(0);
            RunDetails details = await seeding.Query<GetRun, RunDetails>(new GetRun(run.Id), ct);
            details.Output.Text.ShouldContain($"Started by GapActivator (PID {exe.Id}).");

            // The log is dated by the local day; reading both days keeps the check valid across midnight.
            var paths = new GapActivatorPaths(root.Path);
            string[] logFiles = [.. new[] { startDay, DateOnly.FromDateTime(DateTime.Now) }.Distinct().Select(paths.ServiceLogFile)];
            logFiles.Any(File.Exists).ShouldBeTrue();
            string log = string.Concat(logFiles.Where(File.Exists).Select(ReadShared));
            log.ShouldContain("Application started");
        }
        finally
        {
            // Never throws, so a failure of the test above is never replaced by a cleanup failure.
            cleanupFailure = await StopTreeAsync(exe);
        }

        cleanupFailure.ShouldBeNull();
    }

    /// <summary>Kills the executable's process tree and waits for it to exit; returns why that failed, or null.</summary>
    private static async Task<string?> StopTreeAsync(DiagnosticsProcess exe)
    {
        try
        {
            if (!exe.HasExited)
            {
                exe.Kill(entireProcessTree: true);
            }

            await exe.WaitForExitAsync(CancellationToken.None).WaitAsync(ExitWait, CancellationToken.None);
            return null;
        }
        catch (Exception e)
        {
            return $"The service executable (PID {exe.Id}) was not stopped: {e.GetType().Name}: {e.Message}";
        }
    }

    private static void Append(StringBuilder console, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (console)
        {
            console.AppendLine(line);
        }
    }

    private static string Read(StringBuilder console)
    {
        lock (console)
        {
            return console.ToString();
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
