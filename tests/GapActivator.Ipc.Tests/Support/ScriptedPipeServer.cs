using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json.Serialization.Metadata;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Ipc.Tests.Support;

/// <summary>A raw single-connection pipe server on a unique name: it records frames, answers through a delegate and can push raw bytes.</summary>
public sealed class ScriptedPipeServer : IAsyncDisposable
{
    public static readonly HelloResponse DefaultHello =
        new(IpcProtocol.Version, "9.9.9", new CallerDto("TEST\\caller", true), OperatorsGroupMode.Disabled);

    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<RequestEnvelope> _received = [];
    private readonly Func<ScriptedPipeServer, RequestEnvelope, Task>? _onRequest;
    private readonly ServerEnvelope? _helloReply;

    /// <param name="onRequest">Called for every request but Hello.</param>
    /// <param name="helloReply">The answer to the first Hello; the default is a success with <see cref="DefaultHello"/>.</param>
    public ScriptedPipeServer(
        Func<ScriptedPipeServer, RequestEnvelope, Task>? onRequest = null,
        ServerEnvelope? helloReply = null)
    {
        PipeName = $"GapActivator.Tests.{Guid.NewGuid():N}";
        _onRequest = onRequest;
        _helloReply = helloReply;
        _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Completion = Task.Run(RunAsync);
    }

    public string PipeName { get; }

    /// <summary>Completes when the connection ends, from either side.</summary>
    public Task Completion { get; }

    public IReadOnlyList<RequestEnvelope> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>A header that declares a payload over the maximum.</summary>
    public static byte[] OversizedHeader()
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, IpcProtocol.MaxPayloadBytes + 1);
        return header;
    }

    public async Task WaitForRequestsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Received.Count < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public Task RespondAsync<T>(int id, T payload, JsonTypeInfo<T> type) =>
        WriteAsync(ServerEnvelope.Response(id, IpcCodec.ToElement(payload, type)));

    public Task FailAsync(int id, IpcError error) => WriteAsync(ServerEnvelope.Failure(id, error));

    public Task PushAsync<T>(string eventName, T payload, JsonTypeInfo<T> type) =>
        WriteAsync(ServerEnvelope.Push(eventName, IpcCodec.ToElement(payload, type)));

    public async Task WriteAsync(ServerEnvelope envelope)
    {
        byte[] bytes = IpcCodec.Serialize(envelope, IpcJsonContext.Default.ServerEnvelope);
        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            await IpcFrame.WriteAsync(_pipe, bytes, _cts.Token);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendRawAsync(byte[] bytes)
    {
        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            await _pipe.WriteAsync(bytes, _cts.Token);
            await _pipe.FlushAsync(_cts.Token);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Close() => _pipe.Dispose();

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _pipe.Dispose();
        try
        {
            await Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Never leave the dispose hanging; tests that care wait on Completion themselves.
        }

        _cts.Dispose();
        _writeLock.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await _pipe.WaitForConnectionAsync(_cts.Token);
            bool helloSeen = false;
            while (await IpcFrame.ReadAsync(_pipe, _cts.Token) is { } frame)
            {
                RequestEnvelope request = IpcCodec.ParseRequest(frame);
                lock (_received)
                {
                    _received.Add(request);
                }

                if (request.Type == IpcMessages.Hello.Name && !helloSeen)
                {
                    helloSeen = true;
                    await WriteAsync(_helloReply
                        ?? ServerEnvelope.Response(request.Id, IpcCodec.ToElement(DefaultHello, IpcJsonContext.Default.HelloResponse)));
                }
                else if (_onRequest is not null)
                {
                    await _onRequest(this, request);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or IpcProtocolException)
        {
            // The connection ended; nothing to report.
        }
    }
}
