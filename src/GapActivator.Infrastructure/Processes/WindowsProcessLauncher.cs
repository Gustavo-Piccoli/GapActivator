using System.ComponentModel;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Infrastructure.Processes;

/// <summary>
/// Starts children suspended through <c>CreateProcessW</c>, so the job object and the cancel event exist before the
/// child runs (spec section 7). stdout and stderr share one pipe, which keeps the child's write order in the output file.
/// After a restart, <see cref="TryAttach"/> re-adopts a child by its PID and start time.
/// </summary>
internal sealed partial class WindowsProcessLauncher(InfrastructureOptions options, ILogger<WindowsProcessLauncher> logger)
    : IProcessLauncher
{
    private const uint CreationFlags =
        NativeMethods.CREATE_SUSPENDED | NativeMethods.CREATE_NO_WINDOW | NativeMethods.CREATE_UNICODE_ENVIRONMENT;

    private const uint AttachAccess =
        NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_TERMINATE;

    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A live process whose start time differs from the recorded one by more than this has a reused PID.</summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Every inheritable handle is created, inherited and closed under this lock, so a concurrent launch never passes
    /// another child's pipe to its own child (which would keep that pipe open after the other child exits).
    /// </summary>
    private static readonly Lock CreateProcessGate = new();

    public async Task<IRunningProcess> LaunchAsync(LaunchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        // cmd.exe would start and fail on a missing script, which hides the missing file behind exit code 1.
        if (CommandLineBuilder.IsBatchFile(request.Executable) && !File.Exists(request.Executable))
        {
            throw new LaunchFailedException(NotFound(request.Executable));
        }

        (string application, string commandLine) = CommandLineBuilder.Build(request.Executable, request.Arguments);
        char[] environment = EnvironmentBlockBuilder.Build(
            Environment.GetEnvironmentVariables(), request.Environment, ParentIdentity.Current);

        FileStream? output = OpenOutput(request.RunId);
        string pipeName = $"GapActivator.Output.{Guid.NewGuid():N}";
        NamedPipeServerStream? pipe = null;
        NativeMethods.PROCESS_INFORMATION created;
        try
        {
            pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            created = Create(request, application, commandLine, environment, pipeName);
        }
        catch
        {
            pipe?.Dispose();
            output?.Dispose();
            throw;
        }

        // From here the OS process exists, so the token is ignored.
        var process = new SafeProcessHandle(created.hProcess, ownsHandle: true);
        int pid = created.dwProcessId;
        SafeJobHandle? job = null;
        EventWaitHandle? cancelEvent = null;
        DateTimeOffset startTime;
        try
        {
            // The child holds the client end, so this completes at once.
            await pipe.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);

            // The start time names the job, so it is read first, while the child is still suspended.
            startTime = StartTimeOf(process);
            job = CreateJob(process, pid, startTime);

            cancelEvent = new EventWaitHandle(false, EventResetMode.ManualReset, KernelObjectNames.CancelEvent(pid), out bool isNew);
            if (!isNew)
            {
                // A stale event left for a reused PID must not cancel the new child.
                cancelEvent.Reset();
            }

            if (NativeMethods.ResumeThread(created.hThread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch (Exception ex)
        {
            // Never leave a suspended child behind.
            NativeMethods.TerminateProcess(process, 1);
            NativeMethods.CloseHandle(created.hThread);
            cancelEvent?.Dispose();
            job?.Dispose();
            process.Dispose();
            await pipe.DisposeAsync().ConfigureAwait(false);
            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }

            throw new LaunchFailedException($"The process could not be prepared: {ex.Message}", ex);
        }

        NativeMethods.CloseHandle(created.hThread);
        RunOutputPump? pump = null;
        try
        {
            pump = new RunOutputPump(pipe, output, options.RunOutputMaxBytes, request.RunId, logger);
            return new WindowsRunningProcess(pid, startTime, process, job, cancelEvent, pump, options.KillWaitTimeout, logger);
        }
        catch (Exception ex)
        {
            // The child is already running: never leave it untracked.
            NativeMethods.TerminateProcess(process, 1);
            if (pump is not null)
            {
                await pump.DisposeAsync().ConfigureAwait(false);
            }

            cancelEvent.Dispose();
            job.Dispose();
            process.Dispose();
            await pipe.DisposeAsync().ConfigureAwait(false);
            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }

            throw new LaunchFailedException($"The process could not be tracked: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates the child's job, named with its PID and start time, and assigns the child to it. A job left for an
    /// earlier process with the same PID has another name, so it is never joined (ruling C24).
    /// </summary>
    internal static SafeJobHandle CreateJob(SafeProcessHandle process, int pid, DateTimeOffset startTime)
    {
        SafeJobHandle job = NativeMethods.CreateJobObject(0, KernelObjectNames.Job(pid, startTime));
        if (job.IsInvalid || !NativeMethods.AssignProcessToJobObject(job, process))
        {
            int error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new Win32Exception(error);
        }

        return job;
    }

    /// <summary>
    /// Re-adopts a child of the previous service instance. The output is not captured again: the note appended to the
    /// run output says so (ruling 7).
    /// </summary>
    /// <returns>
    /// Null when no process has the PID, when that process is no longer running, when its start time differs from
    /// <paramref name="processStartTime"/> by more than 1 ms (the PID was reused), or when it cannot be opened (access
    /// denied: not a process this service account launched; logged as a Warning).
    /// </returns>
    /// <exception cref="Win32Exception">An unexpected Win32 failure.</exception>
    /// <remarks>
    /// Every failure other than those above throws, including one opening the cancel event: an unknown state is never
    /// reported as gone (ruling C15).
    /// </remarks>
    public IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime)
    {
        SafeProcessHandle process = NativeMethods.OpenProcess(AttachAccess, false, pid);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            process.Dispose();
            switch (error)
            {
                case NativeMethods.ERROR_INVALID_PARAMETER:
                    return null;
                case NativeMethods.ERROR_ACCESS_DENIED:
                    LogAttachDenied(logger, runId, pid);
                    return null;
                default:
                    throw new Win32Exception(error);
            }
        }

        SafeJobHandle? job = null;
        EventWaitHandle? cancelEvent = null;
        WindowsRunningProcess? attached = null;
        try
        {
            if (!NativeMethods.GetExitCodeProcess(process, out uint exitCode))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (exitCode != NativeMethods.STILL_ACTIVE)
            {
                return null;
            }

            DateTimeOffset startTime = StartTimeOf(process);
            if ((startTime - processStartTime).Duration() > StartTimeTolerance)
            {
                return null;
            }

            job = OpenJob(process, pid, startTime);

            // Created when missing, and never reset: a cancel signalled before the restart still stands.
            cancelEvent = new EventWaitHandle(false, EventResetMode.ManualReset, KernelObjectNames.CancelEvent(pid));
            AppendReadoptionNote(runId);
            attached = new WindowsRunningProcess(
                pid, startTime, process, job, cancelEvent, pump: null, options.KillWaitTimeout, logger);
            return attached;
        }
        finally
        {
            if (attached is null)
            {
                cancelEvent?.Dispose();
                job?.Dispose();
                process.Dispose();
            }
        }
    }

    private static DateTimeOffset StartTimeOf(SafeProcessHandle process) =>
        NativeMethods.GetProcessTimes(process, out long creation, out _, out _, out _)
            ? DateTimeOffset.FromFileTime(creation).ToUniversalTime()
            : throw new Win32Exception(Marshal.GetLastPInvokeError());

    /// <summary>
    /// Opens the child's job when it still exists and holds the child. Nothing keeps the job's name alive once the
    /// previous instance's handles are closed, so after a crash there is usually none, and the kill walks the process tree.
    /// </summary>
    private static SafeJobHandle? OpenJob(SafeProcessHandle process, int pid, DateTimeOffset startTime)
    {
        SafeJobHandle job = NativeMethods.OpenJobObject(
            NativeMethods.JOB_OBJECT_TERMINATE | NativeMethods.JOB_OBJECT_QUERY, false, KernelObjectNames.Job(pid, startTime));
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            job.Dispose();
            return error == NativeMethods.ERROR_FILE_NOT_FOUND ? null : throw new Win32Exception(error);
        }

        if (!NativeMethods.IsProcessInJob(process, job, out bool inJob))
        {
            int error = Marshal.GetLastPInvokeError();
            job.Dispose();
            throw new Win32Exception(error);
        }

        if (inJob)
        {
            return job;
        }

        job.Dispose();
        return null;
    }

    /// <summary>Best effort: tells the reader of the run output why it stops there.</summary>
    private void AppendReadoptionNote(RunId runId)
    {
        string path = options.Paths.RunOutputFile(runId);
        string note = string.Create(
            CultureInfo.InvariantCulture,
            $"[GapActivator] The service restarted and re-adopted this process at {DateTimeOffset.UtcNow:O}; later output is not captured.\r\n");
        try
        {
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            file.Write(Encoding.UTF8.GetBytes(note));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogReadoptionNoteUnavailable(logger, ex, runId, path);
        }
    }

    /// <summary>Creates the child suspended, with stdin on NUL and stdout and stderr on the write end of the pipe.</summary>
    private static unsafe NativeMethods.PROCESS_INFORMATION Create(
        LaunchRequest request, string application, string commandLine, char[] environment, string pipeName)
    {
        // CreateProcessW may write to the command line buffer.
        char[] line = (commandLine + "\0").ToCharArray();
        lock (CreateProcessGate)
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.None, HandleInheritability.Inheritable);
            client.Connect(PipeConnectTimeout);
            using SafeFileHandle input = File.OpenHandle(
                "NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Inheritable);

            var startup = new NativeMethods.STARTUPINFOW
            {
                cb = sizeof(NativeMethods.STARTUPINFOW),
                dwFlags = NativeMethods.STARTF_USESTDHANDLES,
                hStdInput = input.DangerousGetHandle(),
                hStdOutput = client.SafePipeHandle.DangerousGetHandle(),
                hStdError = client.SafePipeHandle.DangerousGetHandle(),
            };

            NativeMethods.PROCESS_INFORMATION created;
            bool started;
            int error;
            fixed (char* commandLinePointer = line)
            fixed (char* environmentPointer = environment)
            {
                started = NativeMethods.CreateProcess(
                    application, commandLinePointer, 0, 0, bInheritHandles: true, CreationFlags, environmentPointer,
                    request.WorkingDirectory, in startup, out created);
                error = Marshal.GetLastPInvokeError();
            }

            if (!started)
            {
                throw new LaunchFailedException(DescribeStartFailure(request, error), new Win32Exception(error));
            }

            return created;
        }
    }

    private static string DescribeStartFailure(LaunchRequest request, int error) => error switch
    {
        NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND => NotFound(request.Executable),
        NativeMethods.ERROR_ACCESS_DENIED => $"Access to '{request.Executable}' was denied for the service account.",
        NativeMethods.ERROR_BAD_EXE_FORMAT => $"'{request.Executable}' is not a valid Windows program.",
        NativeMethods.ERROR_DIRECTORY => $"The working directory '{request.WorkingDirectory}' does not exist.",
        _ => $"The process could not be started: {new Win32Exception(error).Message} (error {error}).",
    };

    private static string NotFound(string executable) => $"The executable '{executable}' was not found.";

    /// <summary>Best effort: when the run output file cannot be opened, the child still runs and its output is discarded.</summary>
    private FileStream? OpenOutput(RunId runId)
    {
        string path = options.Paths.RunOutputFile(runId);
        try
        {
            return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogOutputUnavailable(logger, ex, runId, path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The output of run {RunId} cannot be written to {Path}; it is discarded.")]
    private static partial void LogOutputUnavailable(ILogger logger, Exception exception, RunId runId, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process {Pid} of run {RunId} cannot be opened (access denied); it is not one this service launched, so the run's child counts as gone.")]
    private static partial void LogAttachDenied(ILogger logger, RunId runId, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The re-adoption note of run {RunId} cannot be written to {Path}.")]
    private static partial void LogReadoptionNoteUnavailable(ILogger logger, Exception exception, RunId runId, string path);
}
