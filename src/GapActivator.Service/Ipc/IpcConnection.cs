using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using GapActivator.Application.Messaging;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Service.Ipc;

/// <summary>
/// One pipe client: the Hello handshake, then a request loop that runs up to <see cref="IpcOptions.MaxRequestsInFlight"/>
/// requests at once and writes each response under one write lock. A request runs with the service's stopping token, not
/// the connection's: a client that leaves never cancels a command.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The semaphores and the cancellation source hold no wait handle or timer, and a request that outlives an aborted connection still releases its slot.")]
internal sealed partial class IpcConnection(
    NamedPipeServerStream pipe,
    IpcOptions options,
    IpcRequestRouter router,
    ICallerResolver callers,
    IpcConnectionRegistry registry,
    HostRestart restart,
    ILogger<IpcConnection> logger)
{
    private readonly HostRestart _restart = restart;

    private const string Unidentified = "(unidentified)";

    // Client-controlled text is cut and cleaned before it reaches the line-oriented log.
    private const int MaxLoggedClientVersion = 64;
    private const int MaxLoggedReason = 200;

    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _inFlight = new(options.MaxRequestsInFlight, options.MaxRequestsInFlight);
    private readonly ConcurrentDictionary<long, (string Type, Task Done)> _running = new();
    private readonly Channel<ServerEnvelope> _pushes = Channel.CreateBounded<ServerEnvelope>(
        new BoundedChannelOptions(Math.Max(1, options.PushQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private StateChangedCoalescer? _coalescer;

    private CancellationToken _stopping;
    private CancellationToken _readToken;
    private Caller? _caller;
    private long _nextHandler;
    private int _ended;
    private volatile bool _pushReady;

    private string CallerName => _caller?.Name ?? Unidentified;

    /// <summary>The types of the requests that have not finished, for the shutdown warning.</summary>
    public IReadOnlyList<string> RunningHandlers => [.. _running.Values.Select(r => r.Type)];

    /// <summary>Runs until the client leaves, the protocol is violated or <paramref name="stoppingToken"/> fires and the requests end.</summary>
    /// <param name="admitted">False when the registry refused the connection: it only answers <c>busy</c>.</param>
    public async Task RunAsync(bool admitted, CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        using CancellationTokenSource readSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _closed.Token);
        _readToken = readSource.Token;
        try
        {
            if (!admitted)
            {
                await RefuseAsync().ConfigureAwait(false);
                return;
            }

            if (!await HandshakeAsync().ConfigureAwait(false))
            {
                return;
            }

            await RequestLoopAsync().ConfigureAwait(false);

            // A client that left or broke the protocol ends now; on shutdown the pipe stays open until the requests have
            // answered service-stopping.
            if (!stoppingToken.IsCancellationRequested)
            {
                End();
            }

            await Task.WhenAll(_running.Values.Select(r => r.Done)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogConnectionFailed(ex, CallerName);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Closes the pipe at once; the shutdown does this to a connection that did not end in time.</summary>
    public void Abort() => End();

    /// <summary>
    /// Queues a push for this client. It never waits: a full queue drops its oldest push, and a connection that has not
    /// finished Hello (or has ended) ignores it.
    /// </summary>
    public void EnqueuePush(ServerEnvelope push)
    {
        ArgumentNullException.ThrowIfNull(push);
        if (_pushReady)
        {
            _pushes.Writer.TryWrite(push);
        }
    }

    /// <summary>Hands a state hint to this client's coalescer; ignored before Hello and after the end.</summary>
    public void PostStateChanged(StateChangedDto hint)
    {
        if (_pushReady)
        {
            _coalescer?.Post(hint);
        }
    }

    private async Task RefuseAsync()
    {
        using var timeout = new CancellationTokenSource(options.HelloTimeout, options.Time);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_readToken, timeout.Token);
        try
        {
            // The client is still in its handshake: it sends Hello and waits for the first frame, whatever its id.
            await ReadFrameAsync(linked.Token).ConfigureAwait(false);
        }
        catch (IpcProtocolException)
        {
            // Refused either way.
        }

        await WriteAsync(
            ServerEnvelope.Failure(
                0,
                new IpcError(IpcErrorCodes.Busy, $"GapActivator already serves {options.MaxConnections} connections.", true)))
            .ConfigureAwait(false);
    }

    private async Task<bool> HandshakeAsync()
    {
        using var timeout = new CancellationTokenSource(options.HelloTimeout, options.Time);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_readToken, timeout.Token);

        byte[]? frame;
        try
        {
            frame = await ReadFrameAsync(linked.Token).ConfigureAwait(false);
        }
        catch (IpcProtocolException ex)
        {
            string reason = NotificationText.Sanitize(ex.Message, MaxLoggedReason);
            LogProtocolViolation(CallerName, reason);
            return false;
        }

        if (frame is null)
        {
            if (timeout.IsCancellationRequested)
            {
                LogNoHello(options.HelloTimeout);
            }

            return false;
        }

        RequestEnvelope request;
        HelloRequest hello;
        try
        {
            request = IpcCodec.ParseRequest(frame);
        }
        catch (IpcProtocolException)
        {
            return await RejectHelloAsync(0, IpcErrorCodes.ProtocolMismatch, "The first message must be Hello.").ConfigureAwait(false);
        }

        try
        {
            hello = request.Type == IpcMessages.Hello.Name
                ? IpcCodec.ReadPayload(request.Payload, IpcMessages.Hello.RequestType)
                : throw new JsonException("Not Hello.");
        }
        catch (JsonException)
        {
            return await RejectHelloAsync(request.Id, IpcErrorCodes.ProtocolMismatch, "The first message must be Hello.").ConfigureAwait(false);
        }

        if (!IpcProtocol.IsSupported(hello.ProtocolVersion))
        {
            return await RejectHelloAsync(
                request.Id,
                IpcErrorCodes.ProtocolMismatch,
                $"The service speaks protocol version {IpcProtocol.Version}; the client sent {hello.ProtocolVersion}.").ConfigureAwait(false);
        }

        Caller caller;
        try
        {
            caller = callers.Resolve(pipe);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the client is not identified and gets nothing else; the log tells the two causes apart.
            if (ex is UnauthorizedAccessException)
            {
                LogClientNotIdentified();
            }
            else
            {
                LogResolverFailed(ex);
            }

            return await RejectHelloAsync(request.Id, IpcErrorCodes.Unauthorized, "The client could not be identified.").ConfigureAwait(false);
        }

        _caller = caller;
        var response = new HelloResponse(
            IpcProtocol.Version,
            ServiceInfo.Version,
            new CallerDto(caller.Name, caller.CanChange),
            callers.OperatorsGroupEnabled ? OperatorsGroupMode.Enabled : OperatorsGroupMode.Disabled);
        bool written = await WriteAsync(
            ServerEnvelope.Response(request.Id, IpcCodec.ToElement(response, IpcMessages.Hello.ResponseType))).ConfigureAwait(false);
        if (written)
        {
            string clientVersion = NotificationText.Sanitize(hello.ClientVersion, MaxLoggedClientVersion);
            LogConnected(caller.Name, clientVersion);
            StartPushing();
        }

        return written;
    }

    /// <summary>
    /// From here on the client gets pushes. It starts after the Hello response is on the wire, so that response is always
    /// the first frame; the pushes then use the same write path as the responses.
    /// </summary>
    private void StartPushing()
    {
        _coalescer = new StateChangedCoalescer(
            options.StateChangedInterval,
            options.Time,
            hint => _pushes.Writer.TryWrite(
                ServerEnvelope.Push(IpcEvents.StateChanged, IpcCodec.ToElement(hint, IpcJsonContext.Default.StateChangedDto))));
        _pushReady = true;
        _ = Task.Run(PushLoopAsync, CancellationToken.None);

        // The connection may have ended while this was being set up.
        if (_closed.IsCancellationRequested)
        {
            _coalescer.Dispose();
        }
    }

    private async Task PushLoopAsync()
    {
        try
        {
            await foreach (ServerEnvelope push in _pushes.Reader.ReadAllAsync(_closed.Token).ConfigureAwait(false))
            {
                if (!await WriteAsync(push).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The connection ended.
        }
        catch (Exception ex)
        {
            LogConnectionFailed(ex, CallerName);
        }
    }

    private async Task<bool> RejectHelloAsync(int id, string code, string message)
    {
        await WriteAsync(ServerEnvelope.Failure(id, new IpcError(code, message, false))).ConfigureAwait(false);
        return false;
    }

    private async Task RequestLoopAsync()
    {
        while (true)
        {
            RequestEnvelope request;
            try
            {
                byte[]? frame = await ReadFrameAsync(_readToken).ConfigureAwait(false);
                if (frame is null)
                {
                    return;
                }

                request = IpcCodec.ParseRequest(frame);
            }
            catch (IpcProtocolException ex)
            {
                string reason = NotificationText.Sanitize(ex.Message, MaxLoggedReason);
                LogProtocolViolation(CallerName, reason);
                return;
            }

            if (request.Type == IpcMessages.Hello.Name)
            {
                await WriteAsync(
                    ServerEnvelope.Failure(request.Id, new IpcError(IpcErrorCodes.Invalid, "Hello was already received.", false)))
                    .ConfigureAwait(false);
                continue;
            }

            try
            {
                await _inFlight.WaitAsync(_readToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            StartHandler(request);
        }
    }

    private void StartHandler(RequestEnvelope request)
    {
        long key = Interlocked.Increment(ref _nextHandler);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running[key] = (NotificationText.Sanitize(request.Type!, MaxLoggedReason), done.Task);
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogConnectionFailed(ex, CallerName);
            }
            finally
            {
                _running.TryRemove(key, out _);
                _inFlight.Release();
                done.SetResult();
            }
        }, CancellationToken.None);
    }

    private async Task HandleAsync(RequestEnvelope request)
    {
        string type = request.Type!;
        string loggedType = NotificationText.Sanitize(type, MaxLoggedReason);
        ServerEnvelope response;
        IDisposable? deferral = null;
        try
        {
            try
            {
                IpcRoute route = router.Find(type);
                if (route.DefersRestart)
                {
                    // Held until the response is on the wire: the dispatcher ends the restart request's scope before then.
                    deferral = _restart.Defer();
                }

                JsonElement payload = await router.DispatchAsync(route, request.Payload, _caller!, _stopping).ConfigureAwait(false);
                response = ServerEnvelope.Response(request.Id, payload);
            }
            catch (Exception ex)
            {
                bool stopping = _stopping.IsCancellationRequested;
                if (IpcErrorMapper.IsUnexpected(ex, stopping))
                {
                    LogRequestFailed(ex, request.Id, loggedType, CallerName);
                }

                response = ServerEnvelope.Failure(request.Id, IpcErrorMapper.Map(ex, type, stopping));
            }

            // The order matters to whoever defers something until the response is on the wire: write the frame, log it, and
            // only then release what was held back for it (also when the write failed).
            if (await WriteAsync(response).ConfigureAwait(false))
            {
                LogResponseWritten(request.Id, loggedType);
            }
        }
        finally
        {
            deferral?.Dispose();
        }
    }

    /// <returns>Null when the client left, the connection closed or the service is stopping.</returns>
    /// <exception cref="IpcProtocolException">A bad frame.</exception>
    private async Task<byte[]?> ReadFrameAsync(CancellationToken ct)
    {
        try
        {
            return await IpcFrame.ReadAsync(pipe, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <returns>True when the frame was written; false when the pipe is gone or the client stopped reading.</returns>
    private async Task<bool> WriteAsync(ServerEnvelope envelope)
    {
        // Serialized before the lock, so a large response never holds other writers back.
        byte[] bytes = IpcCodec.Serialize(envelope, IpcJsonContext.Default.ServerEnvelope);
        if (bytes.Length > IpcProtocol.MaxPayloadBytes)
        {
            bytes = IpcCodec.Serialize(
                ServerEnvelope.Failure(envelope.Id ?? 0, new IpcError(IpcErrorCodes.Internal, "The response is too large.", false)),
                IpcJsonContext.Default.ServerEnvelope);
        }

        using var timeout = new CancellationTokenSource(options.WriteTimeout, options.Time);
        try
        {
            await _writeLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await IpcFrame.WriteAsync(pipe, bytes, timeout.Token).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A half-written frame would corrupt the stream, so the connection goes.
            LogStoppedReading(CallerName);
            End();
            return false;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // A write to a closed pipe is dropped: the client left.
            return false;
        }
    }

    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return;
        }

        _pushReady = false;
        _closed.Cancel();
        _pushes.Writer.TryComplete();
        _coalescer?.Dispose();
        pipe.Dispose();
        registry.Remove(this);
        if (_caller is not null)
        {
            LogDisconnected(_caller.Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A pipe client could not be identified; the connection is refused.")]
    private partial void LogClientNotIdentified();

    [LoggerMessage(Level = LogLevel.Error, Message = "Resolving a pipe client failed; the connection is refused.")]
    private partial void LogResolverFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipe client {Caller} connected (client {ClientVersion}).")]
    private partial void LogConnected(string caller, string clientVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipe client {Caller} disconnected.")]
    private partial void LogDisconnected(string caller);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipe client {Caller} broke the protocol; the connection is closed: {Reason}")]
    private partial void LogProtocolViolation(string caller, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A pipe client sent no Hello within {Timeout}; the connection is closed.")]
    private partial void LogNoHello(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipe client {Caller} stopped reading; the connection is dropped.")]
    private partial void LogStoppedReading(string caller);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request {Id} ({Type}) from {Caller} failed.")]
    private partial void LogRequestFailed(Exception exception, int id, string type, string caller);

    [LoggerMessage(Level = LogLevel.Error, Message = "The connection of pipe client {Caller} failed.")]
    private partial void LogConnectionFailed(Exception exception, string caller);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Response {Id} to {Type} was written.")]
    private partial void LogResponseWritten(int id, string type);
}
