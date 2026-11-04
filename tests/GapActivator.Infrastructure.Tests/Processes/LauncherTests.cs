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
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Infrastructure.Tests.Processes;

[Trait("Category", "Integration")]
public sealed partial class LauncherTests : IAsyncLifetime
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OutputWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GrandchildFileWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GoneWait = TimeSpan.FromSeconds(5);

    private static readonly string[] TestChildFiles =
        [.. new[] { ".exe", ".dll", ".runtimeconfig.json", ".deps.json" }.Select(e => "GapActivator.TestChild" + e)];

    private readonly TempDataRoot _root = new();
    private readonly LogRecorder _logs = new();
    private readonly List<IRunningProcess> _launched = [];
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
        }

        foreach (IRunningProcess process in _launched)
        {
            await process.DisposeAsync();
        }

        await _root.DisposeAsync();
    }

    [Fact]
    public async Task Launch_ExitCode_IsReported()
    {
        Launched run = await LaunchAsync("exit 7");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(7);
    }

    // BI-084
    [Fact]
    public async Task Launch_StdoutAndStderr_AreCapturedInWriteOrder()
    {
        Launched run = await LaunchAsync("print-both");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        ReadOutput(run.OutputFile).ShouldBe("out-1\r\nerr-1\r\nout-2\r\n");
    }

    // BI-045
    [Fact]
    public async Task Launch_PathsAndArgumentsWithSpacesAndNonAscii_PassedVerbatim()
    {
        string toolDirectory = Path.Combine(_root.Path, "Tool Ü dir");
        string workingDirectory = Path.Combine(_root.Path, "work dir ç");
        Directory.CreateDirectory(toolDirectory);
        Directory.CreateDirectory(workingDirectory);
        foreach (string file in TestChildFiles)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(toolDirectory, file));
        }

        string executable = Path.Combine(toolDirectory, "GapActivator.TestChild.exe");

        Launched echo = await LaunchAsync("echo-args \"a b\" c", executable, workingDirectory);
        Launched cwd = await LaunchAsync("cwd", executable, workingDirectory);

        (await echo.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        (await cwd.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        ReadOutput(echo.OutputFile).ShouldBe("arg:a b\r\narg:c\r\n");
        ReadOutput(cwd.OutputFile).ShouldBe($"cwd:{workingDirectory}\r\n");
    }

    // BI-060
    [Fact]
    public async Task Launch_Environment_ContainsParentIdentityInContractFormat()
    {
        Launched run = await LaunchAsync("env GAPACTIVATOR_PARENT_PID GAPACTIVATOR_PARENT_START_TIME");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        string[] lines = Lines(ReadOutput(run.OutputFile));
        lines.ShouldContain($"env:GAPACTIVATOR_PARENT_PID={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}");
        lines.ShouldContain("envcount:GAPACTIVATOR_PARENT_START_TIME=1");
        string startTime = lines.Single(l => l.StartsWith("env:GAPACTIVATOR_PARENT_START_TIME=", StringComparison.Ordinal))
            ["env:GAPACTIVATOR_PARENT_START_TIME=".Length..];
        startTime.ShouldEndWith("Z");
        DateTime parsed = DateTime.ParseExact(startTime, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        parsed.Kind.ShouldBe(DateTimeKind.Utc);
        parsed.ShouldBe(ParentIdentity.Current.StartTimeUtc);
    }

    [Fact]
    public async Task Launch_RegistrationPathInDifferentCase_ChildSeesOneVariableWithRegistrationValue()
    {
        Environment.GetEnvironmentVariable("PATH").ShouldNotBeNull();

        Launched run = await LaunchAsync("env PATH", environment: [new("path", @"C:\custom")]);

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        string[] lines = Lines(ReadOutput(run.OutputFile));
        lines.ShouldContain(@"env:PATH=C:\custom");
        lines.ShouldContain("envcount:PATH=1");
    }

    // BI-057
    [Fact]
    public async Task Launch_CancelEventExistsBeforeChildRuns()
    {
        Launched run = await LaunchAsync("check-event");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        ReadOutput(run.OutputFile).ShouldBe("event:found\r\n");
    }

    // BI-045
    [Fact]
    public async Task Launch_Child_HasNoConsoleWindow()
    {
        Launched run = await LaunchAsync("console-window");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        ReadOutput(run.OutputFile).ShouldBe("console-window:none\r\n");
    }

    [Fact]
    public async Task Launch_Child_IsInTheJobNamedWithItsPidAndStartTime()
    {
        Launched run = await LaunchAsync("sleep 30000");
        using Process child = Process.GetProcessById(run.Process.Pid);

        using SafeJobHandle job = NativeMethods.OpenJobObject(
            NativeMethods.JOB_OBJECT_QUERY, false, KernelObjectNames.Job(run.Process.Pid, run.Process.StartTime));

        job.IsInvalid.ShouldBeFalse();
        IsInJob(child.SafeHandle, job).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateJob_JobsNamedWithoutOrWithAnotherStartTime_AreNeverJoined()
    {
        Launched run = await LaunchAsync("sleep 30000");
        int pid = run.Process.Pid;
        DateTimeOffset startTime = run.Process.StartTime;
        using Process child = Process.GetProcessById(pid);

        // Jobs left behind for the same PID: one with the old name and one of an earlier process that had this PID.
        using SafeJobHandle oldStyle = NativeMethods.CreateJobObject(0, $@"Local\GapActivator.Job.{pid}");
        using SafeJobHandle otherStartTime = NativeMethods.CreateJobObject(0, KernelObjectNames.Job(pid, startTime.AddSeconds(-1)));
        oldStyle.IsInvalid.ShouldBeFalse();
        otherStartTime.IsInvalid.ShouldBeFalse();
        await run.Process.DisposeAsync();

        using SafeJobHandle job = WindowsProcessLauncher.CreateJob(child.SafeHandle, pid, startTime);

        IsInJob(child.SafeHandle, oldStyle).ShouldBeFalse();
        IsInJob(child.SafeHandle, otherStartTime).ShouldBeFalse();
        IsInJob(child.SafeHandle, job).ShouldBeTrue();
    }

    [Fact]
    public async Task Launch_MissingExecutable_ThrowsLaunchFailedNamingThePath()
    {
        string executable = Path.Combine(_root.Path, "missing tool", "missing.exe");

        LaunchFailedException error = await Should.ThrowAsync<LaunchFailedException>(
            () => NewLauncher().LaunchAsync(Request("exit 0", executable), Ct));

        error.Message.ShouldBe($"The executable '{executable}' was not found.");
    }

    [Fact]
    public async Task Launch_MissingBatchFile_ThrowsLaunchFailedNamingThePath()
    {
        string script = Path.Combine(_root.Path, "missing.cmd");

        LaunchFailedException error = await Should.ThrowAsync<LaunchFailedException>(
            () => NewLauncher().LaunchAsync(Request("", script), Ct));

        error.Message.ShouldBe($"The executable '{script}' was not found.");
    }

    [Fact]
    public async Task Launch_MissingWorkingDirectory_ThrowsLaunchFailed()
    {
        string directory = Path.Combine(_root.Path, "no such dir");

        LaunchFailedException error = await Should.ThrowAsync<LaunchFailedException>(
            () => NewLauncher().LaunchAsync(Request("exit 0", workingDirectory: directory), Ct));

        error.Message.ShouldBe($"The working directory '{directory}' does not exist.");
    }

    [Fact]
    public async Task Launch_TokenAlreadyCancelled_ThrowsOperationCanceledAndStartsNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        LaunchRequest request = Request("exit 0");

        await Should.ThrowAsync<OperationCanceledException>(() => NewLauncher().LaunchAsync(request, cancelled.Token));

        File.Exists(Paths.RunOutputFile(request.RunId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Launch_BatchFile_RunsThroughCmdAndReportsItsExitCode()
    {
        string folder = Path.Combine(_root.Path, "scripts dir");
        Directory.CreateDirectory(folder);
        string script = Path.Combine(folder, "hello world.cmd");
        await File.WriteAllTextAsync(script, "@echo hello from cmd\r\n@exit /b 3\r\n", Ct);

        Launched run = await LaunchAsync("", script, folder);

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(3);
        ReadOutput(run.OutputFile).ShouldContain("hello from cmd");
    }

    // BI-085
    [Fact]
    public async Task Launch_OutputFolderUnwritable_ChildStillRuns()
    {
        Directory.Delete(Paths.RunLogsDirectory);
        await File.WriteAllTextAsync(Paths.RunLogsDirectory, "not a folder", Ct);

        Launched run = await LaunchAsync("exit 0");

        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);
        _logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(run.OutputFile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Output_OverTheCap_IsTruncatedWithMarkerAndChildNotBlocked()
    {
        const string marker = "\r\n[GapActivator] Output truncated after 1000 bytes.\r\n";

        Launched run = await LaunchAsync("flood 100000", launcher: NewLauncher(maxOutputBytes: 1000));

        (await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBe(0);
        string output = ReadOutput(run.OutputFile);
        output.Length.ShouldBeLessThanOrEqualTo(1000 + marker.Length);
        output.ShouldEndWith(marker);
        output.ShouldBe(new string('x', 1000) + marker);
    }

    [Fact]
    public async Task Exited_GrandchildKeepsStdoutOpen_CompletesAtChildExit()
    {
        Launched run = await LaunchAsync("spawn-grandchild exit");
        Process? grandchild = null;
        try
        {
            (await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(0);
            grandchild = await GrandchildAsync(run);

            // The output pipe is still open, held by the grandchild: this is the case under test.
            ((WindowsRunningProcess)run.Process).OutputDrained.IsCompleted.ShouldBeFalse();
            await run.Process.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);
            grandchild.HasExited.ShouldBeFalse();
        }
        finally
        {
            Kill(grandchild);
        }
    }

    // BI-057
    [Fact]
    public async Task SignalCancel_ChildHonoringEvent_ExitsCleanly()
    {
        Launched run = await LaunchAsync("honor-cancel");
        await WaitForOutputAsync(run.OutputFile, "ready\r\n");

        run.Process.SignalCancel();

        (await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(0);
        ReadOutput(run.OutputFile).ShouldBe("ready\r\ncancelled\r\n");
    }

    [Fact]
    public async Task SignalCancel_Twice_IsIdempotent()
    {
        Launched run = await LaunchAsync("honor-cancel");
        await WaitForOutputAsync(run.OutputFile, "ready\r\n");

        run.Process.SignalCancel();
        run.Process.SignalCancel();

        (await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBe(0);
        ReadOutput(run.OutputFile).ShouldBe("ready\r\ncancelled\r\n");
        await run.Process.DisposeAsync();
        Should.NotThrow(run.Process.SignalCancel);
    }

    [Fact]
    public async Task Launch_FailureAfterResume_KillsTheChildAndItsGrandchildThroughTheJob()
    {
        string file = Path.Combine(_root.Path, "gc.txt");
        int childPid = 0;
        int grandchildPid = 0;
        var launcher = new WindowsProcessLauncher(new InfrastructureOptions(Paths), new RecordingLogger<WindowsProcessLauncher>(_logs))
        {
            AfterResume = pid =>
            {
                childPid = pid;
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(file) && deadline.Elapsed < GrandchildFileWait)
                {
                    Thread.Sleep(25);
                }

                throw new InvalidOperationException("Simulated tracking failure.");
            },
        };

        try
        {
            LaunchFailedException error = await Should.ThrowAsync<LaunchFailedException>(
                () => LaunchAsync("spawn-grandchild-file \"" + file + "\"", launcher: launcher));

            error.Message.ShouldStartWith("The process could not be tracked:");
            childPid.ShouldNotBe(0, "the AfterResume seam was never reached");
            File.Exists(file).ShouldBeTrue();
            grandchildPid = int.Parse(File.ReadAllText(file), CultureInfo.InvariantCulture);
            (await WaitUntilGoneAsync(childPid)).ShouldBeTrue("the child survived");
            (await WaitUntilGoneAsync(grandchildPid)).ShouldBeTrue("the grandchild survived");
        }
        finally
        {
            KillByPid(childPid);
            if (grandchildPid == 0 && File.Exists(file)
                && int.TryParse(File.ReadAllText(file), CultureInfo.InvariantCulture, out int fromFile))
            {
                grandchildPid = fromFile;
            }

            KillByPid(grandchildPid);
        }
    }

    // BI-057
    [Fact]
    public async Task KillTree_ChildIgnoringCancelWithGrandchild_KillsBoth()
    {
        Launched run = await LaunchAsync("spawn-grandchild");
        Process? grandchild = null;
        try
        {
            grandchild = await GrandchildAsync(run);
            run.Process.SignalCancel();

            await run.Process.KillTreeAsync();

            run.Process.Exited.IsCompleted.ShouldBeTrue();
            (await run.Process.Exited).ShouldBe(-1);
            grandchild.WaitForExit(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        }
        finally
        {
            Kill(grandchild);
        }
    }

    [Fact]
    public async Task KillTree_AlreadyExited_ReturnsWithoutError()
    {
        Launched run = await LaunchAsync("exit 0");
        (await run.Process.Exited.WaitAsync(ExitWait, Ct)).ShouldBe(0);

        await run.Process.KillTreeAsync();

        (await run.Process.Exited).ShouldBe(0);
    }

    [Fact]
    public async Task Dispose_WhileRunning_DoesNotKillTheChild()
    {
        Launched run = await LaunchAsync("sleep 30000");
        using Process child = Process.GetProcessById(run.Process.Pid);
        _ = child.SafeHandle;

        await run.Process.DisposeAsync();
        await run.Process.DisposeAsync();

        child.HasExited.ShouldBeFalse();
    }

    private static string ReadOutput(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsInJob(SafeProcessHandle process, SafeJobHandle job)
    {
        NativeMethods.IsProcessInJob(process, job, out bool result).ShouldBeTrue();
        return result;
    }

    private static string[] Lines(string output) => output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

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

    private static async Task<bool> WaitUntilGoneAsync(int pid)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < GoneWait)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
        }

        return false;
    }

    private static void KillByPid(int pid)
    {
        if (pid == 0)
        {
            return;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            Kill(process);
        }
        catch (ArgumentException)
        {
            // Already gone.
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

    [GeneratedRegex(@"grandchild:(\d+)\r\n")]
    private static partial Regex GrandchildLine();

    private async Task<Process> GrandchildAsync(Launched run)
    {
        string output = await WaitForOutputAsync(run.OutputFile, "grandchild:");
        Match match = GrandchildLine().Match(output);
        while (!match.Success)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), Ct);
            match = GrandchildLine().Match(ReadOutput(run.OutputFile));
        }

        Process grandchild = Process.GetProcessById(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        _ = grandchild.SafeHandle;
        _children.Add(grandchild);
        return grandchild;
    }

    private WindowsProcessLauncher NewLauncher(long? maxOutputBytes = null)
    {
        var options = new InfrastructureOptions(Paths);
        if (maxOutputBytes is { } max)
        {
            options = options with { RunOutputMaxBytes = max };
        }

        return new WindowsProcessLauncher(options, new RecordingLogger<WindowsProcessLauncher>(_logs));
    }

    private static LaunchRequest Request(
        string arguments,
        string? executable = null,
        string? workingDirectory = null,
        IReadOnlyList<KeyValuePair<string, string>>? environment = null) =>
        new(new RunId(Guid.CreateVersion7()), executable ?? ChildPaths.TestChild, arguments,
            workingDirectory ?? AppContext.BaseDirectory, environment ?? []);

    private async Task<Launched> LaunchAsync(
        string arguments,
        string? executable = null,
        string? workingDirectory = null,
        IReadOnlyList<KeyValuePair<string, string>>? environment = null,
        WindowsProcessLauncher? launcher = null)
    {
        LaunchRequest request = Request(arguments, executable, workingDirectory, environment);
        IRunningProcess process = await (launcher ?? NewLauncher()).LaunchAsync(request, Ct);
        _launched.Add(process);
        try
        {
            Process child = Process.GetProcessById(process.Pid);
            _ = child.SafeHandle;
            _children.Add(child);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // It already exited.
        }

        return new Launched(process, Paths.RunOutputFile(request.RunId));
    }

    private sealed record Launched(IRunningProcess Process, string OutputFile);
}
