using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Tests.Support;

/// <summary>A hand-driven pipe client for the cases the real client never produces.</summary>
internal sealed class RawPipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;

    private RawPipeClient(NamedPipeClientStream pipe) => _pipe = pipe;

    public static async Task<RawPipeClient> ConnectAsync(string pipeName)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(5000);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }

        return new RawPipeClient(pipe);
    }

    public Task SendFrameAsync(byte[] payload) => IpcFrame.WriteAsync(_pipe, payload, CancellationToken.None).AsTask();

    public Task SendTextAsync(string text) => SendFrameAsync(Encoding.UTF8.GetBytes(text));

    /// <summary>Sends <c>{"id":n,"type":"..","payload":..}</c>; the payload is JSON text, or null to leave it out.</summary>
    public Task SendRequestAsync(int id, string type, string? payloadJson = null) =>
        SendTextAsync(payloadJson is null
            ? $$"""{"id":{{id}},"type":"{{type}}"}"""
            : $$"""{"id":{{id}},"type":"{{type}}","payload":{{payloadJson}}}""");

    public Task SendHelloAsync(int protocolVersion = IpcProtocol.Version, int id = 1) =>
        SendRequestAsync(id, "Hello", $$"""{"protocolVersion":{{protocolVersion}},"clientVersion":"raw"}""");

    /// <summary>Sends a frame header only, with any length.</summary>
    public async Task SendHeaderAsync(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        await _pipe.WriteAsync(header);
        await _pipe.FlushAsync();
    }

    /// <summary>Reads one server frame; null when the server closed the pipe.</summary>
    public async Task<ServerEnvelope?> ReadAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            byte[]? frame = await IpcFrame.ReadAsync(_pipe, cts.Token);
            return frame is null ? null : IpcCodec.ParseServer(frame);
        }
        catch (IOException)
        {
            return null;
        }
        catch (IpcProtocolException)
        {
            return null;
        }
    }

    /// <summary>Completes true when the server closes the pipe within the timeout; fails if it sends another frame.</summary>
    public async Task<bool> WaitForEndAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            byte[]? frame = await IpcFrame.ReadAsync(_pipe, cts.Token);
            if (frame is not null)
            {
                throw new InvalidOperationException("The server sent a frame instead of closing.");
            }

            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (IpcProtocolException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<HelloResponse> HelloAsync()
    {
        await SendHelloAsync();
        ServerEnvelope? reply = await ReadAsync(TimeSpan.FromSeconds(5));
        reply.ShouldNotBeNull();
        reply.Ok.ShouldBe(true);
        return IpcCodec.ReadPayload(reply.Payload, IpcJsonContext.Default.HelloResponse);
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();
}
