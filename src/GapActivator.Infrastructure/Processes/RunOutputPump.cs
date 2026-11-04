using System.Globalization;
using System.IO.Pipes;
using System.Text;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Processes;

/// <summary>
/// Copies a child's output pipe into the run output file until the pipe reaches end of file. Past the cap it writes
/// a truncation marker once and keeps draining, so a chatty child never blocks on a full pipe. A failed write stops the
/// writing, never the draining. Owns the pipe and the file.
/// </summary>
internal sealed partial class RunOutputPump : IAsyncDisposable
{
    private const int BufferSize = 16 * 1024;

    private readonly NamedPipeServerStream _pipe;
    private readonly FileStream? _file;
    private readonly long _maxBytes;
    private readonly RunId _runId;
    private readonly ILogger _logger;
    private readonly byte[] _marker;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private long _written;
    private bool _truncated;
    private bool _writeFailed;
    private int _disposed;

    /// <param name="file">The run output file, or null to discard the output.</param>
    public RunOutputPump(NamedPipeServerStream pipe, FileStream? file, long maxBytes, RunId runId, ILogger logger)
    {
        _pipe = pipe;
        _file = file;
        _maxBytes = maxBytes;
        _runId = runId;
        _logger = logger;
        _marker = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"\r\n[GapActivator] Output truncated after {maxBytes} bytes.\r\n"));
        _run = RunAsync();
    }

    /// <summary>
    /// Completes when reading ended: at end of file (the child and every descendant that inherited the pipe closed it),
    /// on a read failure, or when stopped by <see cref="DisposeAsync(TimeSpan)"/>. Never faults.
    /// </summary>
    public Task Drained => _run;

    /// <summary>Waits up to <paramref name="drainWait"/> for end of file, then stops reading and closes the pipe and the file.</summary>
    public async ValueTask DisposeAsync(TimeSpan drainWait)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await _run.WaitAsync(drainWait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _run.ConfigureAwait(false);
        }

        await _pipe.DisposeAsync().ConfigureAwait(false);
        if (_file is not null)
        {
            await _file.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    public ValueTask DisposeAsync() => DisposeAsync(TimeSpan.Zero);

    /// <summary>Never throws: the task ends at end of file, on a read failure or when stopped.</summary>
    private async Task RunAsync()
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            while (true)
            {
                int read = await _pipe.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                Write(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by DisposeAsync.
        }
        catch (IOException ex)
        {
            LogReadFailed(ex, _runId);
        }
    }

    private void Write(ReadOnlySpan<byte> chunk)
    {
        if (_file is null || _writeFailed || _truncated)
        {
            return;
        }

        try
        {
            long room = _maxBytes - _written;
            int kept = (int)Math.Min(room, chunk.Length);
            _file.Write(chunk[..kept]);
            _written += kept;
            if (kept < chunk.Length)
            {
                _file.Write(_marker);
                _truncated = true;
            }

            _file.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writeFailed = true;
            LogWriteFailed(ex, _runId, _file.Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Writing the output of run {RunId} to {Path} failed; the rest of it is discarded.")]
    private partial void LogWriteFailed(Exception exception, RunId runId, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the output of run {RunId} failed; the rest of it is discarded.")]
    private partial void LogReadFailed(Exception exception, RunId runId);
}
