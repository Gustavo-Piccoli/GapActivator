using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Serves the named pipe: waits until the service is ready, then accepts clients and hands each one to an
/// <see cref="IpcConnection"/>. The 16-connection cap lives in <see cref="IpcConnectionRegistry"/>; a connection over the
/// cap is accepted only to be told <c>busy</c>.
/// </summary>
internal sealed partial class IpcServer(
    IpcOptions options,
    IpcRequestRouter router,
    ICallerResolver callers,
    IpcConnectionRegistry registry,
    HostRestart restart,
    ILogger<IpcServer> logger,
    ILogger<IpcConnection> connectionLogger)
{
    private const int PipeBufferSize = 65536;

    // A connection gets its own write timeout to say service-stopping; this is the slack on top of it.
    private static readonly TimeSpan ShutdownSlack = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<long, (IpcConnection Connection, Task Done)> _active = new();
    private long _nextId;

    /// <summary>
    /// Returns, without creating the pipe, when <paramref name="ready"/> faults or is cancelled or the token fires. A pipe
    /// that cannot be created propagates to the caller. Returns when every connection has ended, at most
    /// <see cref="IpcOptions.WriteTimeout"/> plus 5 seconds after the token fired.
    /// </summary>
    public async Task RunAsync(Task ready, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(ready);

        try
        {
            await ready.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed start is the worker's to report; this only declines to serve.
            if (!stoppingToken.IsCancellationRequested)
            {
                string reason = ex.GetType().Name;
                LogNotServing(logger, reason);
            }

            return;
        }

        PipeSecurity security = PipeSecurityFactory.Create(WindowsIdentity.GetCurrent().User!);
        NamedPipeServerStream listening = CreateInstance(security, first: true);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await listening.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The client left between connecting and being accepted.
                    await listening.DisposeAsync().ConfigureAwait(false);
                    listening = CreateInstance(security, first: false);
                    continue;
                }

                NamedPipeServerStream connected = listening;
                try
                {
                    listening = CreateInstance(security, first: false);
                }
                catch
                {
                    await connected.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                Start(connected, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            await listening.DisposeAsync().ConfigureAwait(false);
        }

        await WaitForConnectionsAsync().ConfigureAwait(false);
    }

    private NamedPipeServerStream CreateInstance(PipeSecurity security, bool first) =>
        NamedPipeServerStreamAcl.Create(
            options.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            first ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous,
            PipeBufferSize,
            PipeBufferSize,
            security);

    private void Start(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var connection = new IpcConnection(pipe, options, router, callers, registry, restart, connectionLogger);
        bool admitted = registry.TryAdd(connection, options.MaxConnections);

        long id = Interlocked.Increment(ref _nextId);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _active[id] = (connection, done.Task);
        _ = Task.Run(async () =>
        {
            try
            {
                await connection.RunAsync(admitted, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _active.TryRemove(id, out _);
                done.SetResult();
            }
        }, CancellationToken.None);
    }

    private async Task WaitForConnectionsAsync()
    {
        KeyValuePair<long, (IpcConnection Connection, Task Done)>[] active = _active.ToArray();
        if (active.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(active.Select(a => a.Value.Done))
                .WaitAsync(options.WriteTimeout + ShutdownSlack, options.Time)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            foreach ((long _, (IpcConnection connection, Task done)) in active)
            {
                if (done.IsCompleted)
                {
                    continue;
                }

                LogConnectionStuck(logger, string.Join(", ", connection.RunningHandlers));
                connection.Abort();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The pipe is not served: the service did not become ready ({Reason}).")]
    private static partial void LogNotServing(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A pipe connection did not end in time; its pipe is closed. Requests still running: [{Requests}].")]
    private static partial void LogConnectionStuck(ILogger logger, string requests);
}
