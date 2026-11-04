using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Processes;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Tests.Processes;

/// <summary>
/// Re-adoption after a restart. A service crash is simulated by disposing the first launcher's handle, which closes the
/// process, job and event handles exactly as the death of the service process would; a second launcher then attaches.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class AttachTests : IAsyncLifetime
{
    private static readonly TimeSpan OutputWait = TimeSpan.FromSeconds(10);

    private readonly TempDataRoot _root = new();
    private readonly LogRecorder _logs = new();
    private readonly List<IRunningProcess> _handles = [];
    private readonly List<Process> _children = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GapActivatorPaths Paths => new(_root.Path);

    public ValueTask InitializeAsync()
    {
        Paths.EnsureDirectories();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Process child in _children)
        {
            Kill(child);
            child.Dispose();
        }

        foreach (IRunningProcess handle in _handles)
        {
            await handle.DisposeAsync();
        }

        await _root.DisposeAsync();
    }

    // BI-063, BI-072
    [Fact]
    public async Task TryAttach_LiveChildMatchingStartTime_ReportsExitCode()
    {
        // The sleep outlasts the 2 s the first handle's disposal waits for the output pipe (ruling C12).
        Launched first = await LaunchAndReleaseAsync("sleep 8000 5");

        IRunningProcess? attached = Attach(first);

        attached.ShouldNotBeNull();
        attached.Pid.ShouldBe(first.Pid);
        attached.StartTime.ShouldBe(first.StartTime);
        (await attached.Exited.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldBe(5);
    }

    // BI-063
    [Fact]
    public async Task TryAttach_StartTimeOffByOneSecond_ReturnsNull()
    {
        Launched first = await LaunchAndReleaseAsync("sleep 30000");

        Attach(first, first.StartTime.AddSeconds(1)).ShouldBeNull();
        Attach(first, first.StartTime.AddSeconds(-1)).ShouldBeNull();

        first.Child!.HasExited.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAttach_StartTimeWithinOneMillisecond_Attaches()
    {
        Launched first = await LaunchAndReleaseAsync("sleep 30000");

        Attach(first, first.StartTime.AddTicks(TimeSpan.TicksPerMillisecond)).ShouldNotBeNull();
        Attach(first, first.StartTime.AddTicks(-TimeSpan.TicksPerMillisecond)).ShouldNotBeNull();
    }

    // BI-071
    [Fact]
    public async Task TryAttach_ProcessGone_ReturnsNull()
    {
        // No handle is kept on this child, so its process object is gone once it exited and the launcher released it.
        Launched first = await LaunchAndReleaseAsync("exit 0", keepChild: false, waitForExit: true);

        Attach(first).ShouldBeNull();
    }

    [Fact]
    public async Task TryAttach_ExitedButStillReferenced_ReturnsNull()
    {
        // The test's handle keeps the exited process object, so it can be opened but is no longer running.
        Launched first = await LaunchAndReleaseAsync("exit 0", waitForExit: true);

        Attach(first).ShouldBeNull();
    }

    [Fact]
    public void TryAttach_ProcessOfAnotherAccount_ReturnsNullAndWarns()
    {
        // The System process (PID 4) is protected: it cannot be opened for termination, even by an administrator.
        const int systemPid = 4;
        var runId = new RunId(Guid.CreateVersion7());

        NewLauncher().TryAttach(runId, systemPid, DateTimeOffset.UtcNow).ShouldBeNull();

        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(runId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryAttach_CancelEventNameTakenByAnotherObject_Throws()
    {
        Launched first = await LaunchAndReleaseAsync("sleep 30000");

        // The child never opened its event, so the name died with the first handle and another object can take it.
        using var squatter = new Mutex(false, KernelObjectNames.CancelEvent(first.Pid), out bool created);
        created.ShouldBeTrue();

        Should.Throw<WaitHandleCannotBeOpenedException>(() => Attach(first));
    }

    [Fact]
    public async Task TryAttach_ThenSignalCancel_ChildSeesTheEventFromItsLaunch()
    {
        // The child holds the event from its launch on, which keeps the event alive after the first handle closes.
        Launched first = await LaunchAndReleaseAsync("honor-cancel", waitFor: "ready\r\n");

        IRunningProcess? attached = Attach(first);
        attached.ShouldNotBeNull();
        attached.SignalCancel();

        (await attached.Exited.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task TryAttach_ThenKillTree_KillsChildAndGrandchild()
    {
        Process? grandchild = null;
        try
        {
            Launched first = await LaunchAndReleaseAsync("spawn-grandchild", waitFor: "grandchild:", onOutput: async file =>
                grandchild = await GrandchildAsync(file));

            IRunningProcess? attached = Attach(first);
            attached.ShouldNotBeNull();

            // The job's name died with the first handle, so this kill walks the process tree instead.
            ((WindowsRunningProcess)attached).HasJob.ShouldBeFalse();
            await attached.KillTreeAsync();

            (await attached.Exited).ShouldBe(-1);
            grandchild.ShouldNotBeNull();
            grandchild.WaitForExit(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        }
        finally
        {
            Kill(grandchild);
        }
    }

    [Fact]
    public async Task TryAttach_JobStillOpen_KillsThroughTheJob()
    {
        Launched first = await LaunchAsync("sleep 30000");

        // Another handle keeps the job, and with it its name, alive after the first handle closes.
        using SafeJobHandle job = NativeMethods.OpenJobObject(
            NativeMethods.JOB_OBJECT_QUERY, false, KernelObjectNames.Job(first.Pid, first.StartTime));
        job.IsInvalid.ShouldBeFalse();
        await first.Handle.DisposeAsync();

        IRunningProcess? attached = Attach(first);
        attached.ShouldNotBeNull();
        ((WindowsRunningProcess)attached).HasJob.ShouldBeTrue();
        await attached.KillTreeAsync();

        (await attached.Exited).ShouldBe(-1);
    }

    [Fact]
    public async Task TryAttach_LiveChild_AppendsTheReadoptionNoteToTheRunOutput()
    {
        Launched first = await LaunchAndReleaseAsync("honor-cancel", waitFor: "ready\r\n");
        DateTimeOffset before = DateTimeOffset.UtcNow;

        Attach(first).ShouldNotBeNull();

        DateTimeOffset after = DateTimeOffset.UtcNow;
        string output = ReadOutput(first.OutputFile);
        Match note = ReadoptionNote().Match(output);
        note.Success.ShouldBeTrue(output);
        DateTimeOffset at = DateTimeOffset.ParseExact(note.Groups["at"].Value, "O", CultureInfo.InvariantCulture);
        at.ShouldBeInRange(before, after);
    }

    private static string ReadOutput(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<string> WaitForOutputAsync(string path, string expected)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            string output = File.Exists(path) ? ReadOutput(path) : "";
            if (output.Contains(expected, StringComparison.Ordinal))
            {
                return output;
            }

            if (watch.Elapsed > OutputWait)
            {
                throw new TimeoutException($"The output never contained '{expected}'; it was '{output}'.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
        }
    }

    private static void Kill(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    [GeneratedRegex(@"^ready\r\n\[GapActivator\] The service restarted and re-adopted this process at (?<at>\S+); later output is not captured\.\r\n\z")]
    private static partial Regex ReadoptionNote();

    [GeneratedRegex(@"grandchild:(\d+)\r\n")]
    private static partial Regex GrandchildLine();

    private async Task<Process> GrandchildAsync(string outputFile)
    {
        Match match = GrandchildLine().Match(await WaitForOutputAsync(outputFile, "grandchild:"));
        while (!match.Success)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
            match = GrandchildLine().Match(ReadOutput(outputFile));
        }

        Process grandchild = Process.GetProcessById(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        _ = grandchild.SafeHandle;
        _children.Add(grandchild);
        return grandchild;
    }

    private WindowsProcessLauncher NewLauncher() =>
        new(new InfrastructureOptions(Paths), new RecordingLogger<WindowsProcessLauncher>(_logs));

    /// <summary>Attaches with a new launcher, as the restarted service would; the attached handle is disposed in cleanup.</summary>
    private IRunningProcess? Attach(Launched first, DateTimeOffset? startTime = null)
    {
        IRunningProcess? attached = NewLauncher().TryAttach(first.RunId, first.Pid, startTime ?? first.StartTime);
        if (attached is not null)
        {
            _handles.Add(attached);
        }

        return attached;
    }

    /// <summary>Launches a child; the test keeps its own handle on it (for cleanup) unless told not to.</summary>
    private async Task<Launched> LaunchAsync(string arguments, bool keepChild = true)
    {
        var runId = new RunId(Guid.CreateVersion7());
        var request = new LaunchRequest(runId, ChildPaths.TestChild, arguments, AppContext.BaseDirectory, []);
        IRunningProcess handle = await NewLauncher().LaunchAsync(request, Ct);
        _handles.Add(handle);
        Process? child = null;
        if (keepChild)
        {
            try
            {
                child = Process.GetProcessById(handle.Pid);
                _ = child.SafeHandle;
                _children.Add(child);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // It already exited.
            }
        }

        return new Launched(runId, handle.Pid, handle.StartTime, handle, child, Paths.RunOutputFile(runId));
    }

    /// <summary>Launches a child, waits as asked, then disposes the first handle: the simulated service crash.</summary>
    private async Task<Launched> LaunchAndReleaseAsync(
        string arguments,
        bool keepChild = true,
        bool waitForExit = false,
        string? waitFor = null,
        Func<string, Task>? onOutput = null)
    {
        Launched first = await LaunchAsync(arguments, keepChild);
        if (waitFor is not null)
        {
            await WaitForOutputAsync(first.OutputFile, waitFor);
        }

        if (onOutput is not null)
        {
            await onOutput(first.OutputFile);
        }

        if (waitForExit)
        {
            await first.Handle.Exited.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }

        await first.Handle.DisposeAsync();
        return first;
    }

    private sealed record Launched(
        RunId RunId, int Pid, DateTimeOffset StartTime, IRunningProcess Handle, Process? Child, string OutputFile);
}
