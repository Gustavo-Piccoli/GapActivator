using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Infrastructure.Processes;

/// <summary>
/// Starts children suspended through <c>CreateProcessW</c>, so the job object and the cancel event exist before the
/// child runs (spec section 7). stdout and stderr share one pipe, which keeps the child's write order in the output file.
/// </summary>
internal sealed partial class WindowsProcessLauncher(InfrastructureOptions options, ILogger<WindowsProcessLauncher> logger)
    : IProcessLauncher
{
    private const uint CreationFlags =
        NativeMethods.CREATE_SUSPENDED | NativeMethods.CREATE_NO_WINDOW | NativeMethods.CREATE_UNICODE_ENVIRONMENT;

    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);

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
                pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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

            job = NativeMethods.CreateJobObject(0, KernelObjectNames.Job(pid));
            if (job.IsInvalid || !NativeMethods.AssignProcessToJobObject(job, process))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            cancelEvent = new EventWaitHandle(false, EventResetMode.ManualReset, KernelObjectNames.CancelEvent(pid), out bool isNew);
            if (!isNew)
            {
                // A stale event left for a reused PID must not cancel the new child.
                cancelEvent.Reset();
            }

            if (!NativeMethods.GetProcessTimes(process, out long creation, out _, out _, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            startTime = DateTimeOffset.FromFileTime(creation).ToUniversalTime();

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
        var pump = new RunOutputPump(pipe, output, options.RunOutputMaxBytes, request.RunId, logger);
        return new WindowsRunningProcess(pid, startTime, process, job, cancelEvent, pump, options.KillWaitTimeout, logger);
    }

    /// <summary>Re-adoption after a restart is not available yet.</summary>
    public IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime) =>
        throw new NotSupportedException("Re-adopting a running child is not supported yet.");

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
}
