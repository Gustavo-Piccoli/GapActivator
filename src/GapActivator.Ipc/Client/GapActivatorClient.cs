using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Ipc;

/// <summary>
/// A connection to the GapActivator service over its named pipe. It verifies the server, completes the Hello handshake,
/// matches responses to requests by id and raises the pushes. There is no reconnect: after <see cref="Disconnected"/>
/// the caller connects again.
/// </summary>
public sealed class GapActivatorClient : IAsyncDisposable
{
    private const int HelloId = 1;

    // CancelAfter takes at most about 49 days.
    private static readonly TimeSpan MaxBudget = TimeSpan.FromDays(24);

    [ThreadStatic]
    private static GapActivatorClient? t_raising;

    private readonly NamedPipeClientStream _pipe;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // A null result means the connection ended before the answer came.
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ServerEnvelope?>> _pending = new();
    private readonly TaskCompletionSource<ServerEnvelope?> _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _nextId = HelloId;
    private int _closed;
    private int _disconnectedRaised;
    private bool _handshakeDone;
    private volatile Exception? _failure;
    private Task? _readerTask;

    private GapActivatorClient(NamedPipeClientStream pipe, bool serverVerified)
    {
        _pipe = pipe;
        ServerVerified = serverVerified;
    }

    /// <summary>Raised on the reader loop; handlers must not block.</summary>
    public event EventHandler<NotificationDto>? NotificationReceived;

    /// <summary>Raised on the reader loop; handlers must not block.</summary>
    public event EventHandler<StateChangedDto>? StateChanged;

    /// <summary>Raised exactly once, when the connection ends for any reason (including disposal).</summary>
    public event EventHandler<DisconnectedEventArgs>? Disconnected;

    /// <summary>The server's answer to Hello.</summary>
    public HelloResponse Server { get; private set; } = null!;

    /// <summary>True when the pipe server is the running GapActivator service; false when the service is not installed.</summary>
    public bool ServerVerified { get; }

    /// <exception cref="TimeoutException">Nothing answered within <see cref="GapActivatorClientOptions.ConnectTimeout"/>.</exception>
    /// <exception cref="GapActivatorRequestException">The server is not the service, refused the handshake or speaks another version.</exception>
    public static async Task<GapActivatorClient> ConnectAsync(GapActivatorClientOptions? options = null, CancellationToken ct = default)
    {
        options ??= new GapActivatorClientOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ConnectTimeout, TimeSpan.Zero);
        long start = Stopwatch.GetTimestamp();
        var pipe = new NamedPipeClientStream(
            ".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        GapActivatorClient? client = null;
        try
        {
            await pipe.ConnectAsync((int)Math.Min(Math.Ceiling(options.ConnectTimeout.TotalMilliseconds), int.MaxValue), ct).ConfigureAwait(false);

            bool? verdict = Verify(pipe, options);
            if (verdict == false)
            {
                throw new GapActivatorRequestException(
                    IpcErrorCodes.ServerNotVerified,
                    $"The pipe '{options.PipeName}' is not served by the {options.ServiceName} service.",
                    retryable: false);
            }

            client = new GapActivatorClient(pipe, verdict == true);
            await client.HandshakeAsync(options, start, ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>
    /// Sends one request and awaits its response. Cancelling <paramref name="ct"/> while the request is still waiting for its
    /// response only drops that request. Cancelling it while the frame is being written aborts the connection (a half-written
    /// frame would corrupt the stream): every pending request fails with <c>disconnected</c>, <see cref="Disconnected"/> is
    /// raised and this call throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <exception cref="GapActivatorRequestException">The server rejected the request, or the connection is gone (<c>disconnected</c>).</exception>
    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        IpcMessage<TRequest, TResponse> message,
        TRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();
        ThrowIfClosed();

        int id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<ServerEnvelope?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            // The connection may have ended between the check above and the registration.
            ThrowIfClosed();

            byte[] bytes = IpcCodec.Serialize(
                new RequestEnvelope { Id = id, Type = message.Name, Payload = IpcCodec.ToElement(request, message.RequestType) },
                IpcJsonContext.Default.RequestEnvelope);
            await WriteFrameAsync(bytes, ct).ConfigureAwait(false);

            ServerEnvelope response = await pending.Task.WaitAsync(ct).ConfigureAwait(false) ?? throw Disconnection();
            return ReadResponse(message, response);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task<ProcessSavedDto> RegisterProcessAsync(ProcessDefinitionDto definition, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.RegisterProcess, new RegisterProcessRequest(definition), ct).ConfigureAwait(false);

    public async Task<ProcessSavedDto> UpdateProcessAsync(Guid id, ProcessDefinitionDto definition, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.UpdateProcess, new UpdateProcessRequest(id, definition), ct).ConfigureAwait(false);

    public async Task DeleteProcessAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.DeleteProcess, new DeleteProcessRequest(id), ct).ConfigureAwait(false);

    public async Task EnableProcessAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.EnableProcess, new EnableProcessRequest(id), ct).ConfigureAwait(false);

    public async Task DisableProcessAsync(Guid id, bool cancelActiveRun, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.DisableProcess, new DisableProcessRequest(id, cancelActiveRun), ct).ConfigureAwait(false);

    public async Task UnblockProcessAsync(Guid id, bool runNow, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.UnblockProcess, new UnblockProcessRequest(id, runNow), ct).ConfigureAwait(false);

    public async Task<Guid> RunNowAsync(Guid processId, CancellationToken ct = default) =>
        (await SendAsync(IpcMessages.RunNow, new RunNowRequest(processId), ct).ConfigureAwait(false)).RunId;

    public async Task CancelRunAsync(Guid runId, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.CancelRun, new CancelRunRequest(runId), ct).ConfigureAwait(false);

    public async Task RestartRunAsync(Guid processId, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.RestartRun, new RestartRunRequest(processId), ct).ConfigureAwait(false);

    public async Task PauseInstallationAsync(CancellationToken ct = default) =>
        await SendAsync(IpcMessages.PauseInstallation, new PauseInstallationRequest(), ct).ConfigureAwait(false);

    public async Task ResumeInstallationAsync(CancellationToken ct = default) =>
        await SendAsync(IpcMessages.ResumeInstallation, new ResumeInstallationRequest(), ct).ConfigureAwait(false);

    public async Task UpdateSettingsAsync(SettingsInputDto settings, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.UpdateSettings, new UpdateSettingsRequest(settings), ct).ConfigureAwait(false);

    public async Task SendTestEmailAsync(EmailInputDto email, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.SendTestEmail, new SendTestEmailRequest(email), ct).ConfigureAwait(false);

    public async Task RestartServiceAsync(CancellationToken ct = default) =>
        await SendAsync(IpcMessages.RestartService, new RestartServiceRequest(), ct).ConfigureAwait(false);

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default) =>
        await SendAsync(IpcMessages.GetDashboard, new GetDashboardRequest(), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ProcessSummaryDto>> ListProcessesAsync(CancellationToken ct = default) =>
        (await SendAsync(IpcMessages.ListProcesses, new ListProcessesRequest(), ct).ConfigureAwait(false)).Items;

    public async Task<ProcessDetailsDto> GetProcessAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.GetProcess, new GetProcessRequest(id), ct).ConfigureAwait(false);

    public async Task<RunRowPageDto> ListRunsAsync(RunFilterDto filter, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.ListRuns, new ListRunsRequest(filter), ct).ConfigureAwait(false);

    public async Task<RunDetailsDto> GetRunAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync(IpcMessages.GetRun, new GetRunRequest(id), ct).ConfigureAwait(false);

    public async Task<SettingsViewDto> GetSettingsAsync(CancellationToken ct = default) =>
        await SendAsync(IpcMessages.GetSettings, new GetSettingsRequest(), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(
        string rule,
        DateTimeOffset anchor,
        int count,
        CancellationToken ct = default) =>
        (await SendAsync(IpcMessages.PreviewSchedule, new PreviewScheduleRequest(rule, anchor, count), ct).ConfigureAwait(false)).Occurrences;

    public async ValueTask DisposeAsync()
    {
        bool insideHandler = ReferenceEquals(t_raising, this);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _pipe.DisposeAsync().ConfigureAwait(false);

        if (_readerTask is not { } reader)
        {
            Terminate(null);
        }
        else if (!insideHandler)
        {
            // A Disconnected handler that disposes the client must not wait for the loop it is running on.
            await reader.ConfigureAwait(false);
        }
    }

    /// <returns>null when the service is not installed, otherwise whether the pipe server is the running service.</returns>
    private static bool? Verify(NamedPipeClientStream pipe, GapActivatorClientOptions options)
    {
        // A failed query leaves 0, which no running service has: the verdict is then a rejection unless nothing is installed.
        _ = NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid);
        ServiceLookup lookup = (options.LookupService ?? ScmServiceLookup.Query)(options.ServiceName);
        return ServerVerification.Decide(serverPid, lookup);
    }

    private static TResponse ReadResponse<TRequest, TResponse>(IpcMessage<TRequest, TResponse> message, ServerEnvelope response)
    {
        if (response.Ok == true)
        {
            try
            {
                return IpcCodec.ReadPayload(response.Payload, message.ResponseType);
            }
            catch (JsonException ex)
            {
                throw new GapActivatorRequestException(
                    IpcErrorCodes.Internal, $"The {message.Name} response is invalid: {ex.Message}", retryable: false);
            }
        }

        throw FromError(response.Error);
    }

    private static GapActivatorRequestException FromError(IpcError? error) =>
        error is null
            ? new GapActivatorRequestException(IpcErrorCodes.Internal, "The server reported a failure without details.", retryable: false)
            : new GapActivatorRequestException(error.Code, error.Message, error.Retryable);

    private static GapActivatorRequestException Disconnection() =>
        new(IpcErrorCodes.Disconnected, "The connection to the service is closed.", retryable: true);

    private async Task HandshakeAsync(GapActivatorClientOptions options, long start, CancellationToken ct)
    {
        TimeSpan Remaining() =>
            TimeSpan.FromTicks(Math.Clamp((options.ConnectTimeout - Stopwatch.GetElapsedTime(start)).Ticks, 0, MaxBudget.Ticks));

        _readerTask = Task.Run(ReadLoopAsync, CancellationToken.None);

        byte[] hello = IpcCodec.Serialize(
            new RequestEnvelope
            {
                Id = HelloId,
                Type = IpcMessages.Hello.Name,
                Payload = IpcCodec.ToElement(
                    new HelloRequest(IpcProtocol.Version, options.ClientVersion), IpcJsonContext.Default.HelloRequest),
            },
            IpcJsonContext.Default.RequestEnvelope);

        // The write runs beside the wait for the first frame: a server that never reads must not hold the handshake past
        // ConnectTimeout, and a server that answers without reading (busy) must still be heard.
        using var writeBudget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        writeBudget.CancelAfter(Remaining());
        Task helloWrite = WriteHelloAsync(hello, writeBudget.Token);
        ServerEnvelope first;
        try
        {
            first = await _handshake.Task.WaitAsync(Remaining(), ct).ConfigureAwait(false) ?? throw Disconnection();
        }
        finally
        {
            await writeBudget.CancelAsync().ConfigureAwait(false);
            await helloWrite.ConfigureAwait(false);
        }

        if (first.Ok == false)
        {
            throw FromError(first.Error);
        }

        if (first.Ok != true || first.Id != HelloId)
        {
            throw new GapActivatorRequestException(
                IpcErrorCodes.ProtocolMismatch, "The server answered Hello with an unexpected frame.", retryable: false);
        }

        HelloResponse response;
        try
        {
            response = IpcCodec.ReadPayload(first.Payload, IpcJsonContext.Default.HelloResponse);
        }
        catch (JsonException ex)
        {
            throw new GapActivatorRequestException(
                IpcErrorCodes.ProtocolMismatch, $"The Hello response is invalid: {ex.Message}", retryable: false);
        }

        if (!IpcProtocol.IsSupported(response.ProtocolVersion))
        {
            throw new GapActivatorRequestException(
                IpcErrorCodes.ProtocolMismatch,
                $"The server speaks protocol {response.ProtocolVersion}; this client speaks {IpcProtocol.Version}.",
                retryable: false);
        }

        Server = response;
    }

    /// <summary>
    /// Pre-flight C3: a failed Hello write does not end the handshake and never aborts the connection, so a frame the
    /// server already sent (busy) is still read. Nothing else uses the pipe yet, so no lock is needed.
    /// </summary>
    private async Task WriteHelloAsync(byte[] hello, CancellationToken budget)
    {
        try
        {
            await IpcFrame.WriteAsync(_pipe, hello, budget).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The first server frame, or the end of the budget, decides the handshake.
        }
    }

    /// <summary>
    /// One frame at a time. A cancellation that arrives while the frame is being written aborts the whole connection (a
    /// half-written frame would corrupt the stream); one that arrives while waiting for the write lock does not.
    /// </summary>
    private async Task WriteFrameAsync(byte[] bytes, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            using var writing = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await IpcFrame.WriteAsync(_pipe, bytes, writing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            Abort(ex);
            throw;
        }
        catch (IpcProtocolException ex)
        {
            throw new GapActivatorRequestException(IpcErrorCodes.Invalid, ex.Message, retryable: false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            Abort(ex);
            throw Disconnection();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            throw Disconnection();
        }
    }

    private async Task ReadLoopAsync()
    {
        Exception? error = null;
        try
        {
            while (await IpcFrame.ReadAsync(_pipe, _lifetime.Token).ConfigureAwait(false) is { } frame)
            {
                Dispatch(IpcCodec.ParseServer(frame));
            }
        }
        catch (OperationCanceledException)
        {
            error = _failure;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or IpcProtocolException or JsonException)
        {
            error = ex;
        }
        finally
        {
            Terminate(error ?? _failure);
        }
    }

    private void Dispatch(ServerEnvelope envelope)
    {
        if (!_handshakeDone)
        {
            // Pre-flight C3: until Hello completes, the first server frame completes the handshake whatever its id.
            _handshakeDone = true;
            _handshake.TrySetResult(envelope);
            return;
        }

        if (envelope.Id is { } id)
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetResult(envelope);
            }

            return; // an unknown id is a late response to a cancelled request
        }

        switch (envelope.Event)
        {
            case IpcEvents.Notification:
                Raise(NotificationReceived, IpcCodec.ReadPayload(envelope.Payload, IpcJsonContext.Default.NotificationDto));
                break;
            case IpcEvents.StateChanged:
                Raise(StateChanged, IpcCodec.ReadPayload(envelope.Payload, IpcJsonContext.Default.StateChangedDto));
                break;
        }
    }

    /// <summary>Stops the connection from the sending side; the reader loop then ends and reports <paramref name="error"/>.</summary>
    private void Abort(Exception error)
    {
        _failure ??= error;
        _lifetime.Cancel();
        _pipe.Dispose();
    }

    private void Terminate(Exception? error)
    {
        Interlocked.Exchange(ref _closed, 1);
        _handshake.TrySetResult(null);
        foreach (int id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetResult(null);
            }
        }

        if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
        {
            Raise(Disconnected, new DisconnectedEventArgs(error));
        }
    }

    private void Raise<T>(EventHandler<T>? handler, T args)
    {
        if (handler is null)
        {
            return;
        }

        GapActivatorClient? previous = t_raising;
        t_raising = this;
        try
        {
            handler(this, args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A subscriber's failure must not end the connection.
            Trace.TraceWarning($"A GapActivatorClient event handler threw: {ex}");
        }
        finally
        {
            t_raising = previous;
        }
    }
}
