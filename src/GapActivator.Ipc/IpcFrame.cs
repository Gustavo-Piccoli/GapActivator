using System.Buffers;
using System.Buffers.Binary;

namespace GapActivator.Ipc;

/// <summary>A violation of the wire protocol; the connection that raised it is closed.</summary>
public sealed class IpcProtocolException(string message) : Exception(message);

/// <summary>Length-prefixed frames: a 4-byte little-endian int32 length, then that many bytes of UTF-8 JSON.</summary>
public static class IpcFrame
{
    private const int HeaderSize = 4;

    /// <exception cref="IpcProtocolException">The payload is empty or larger than the maximum; nothing is written.</exception>
    public static async ValueTask WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.Length == 0 || payload.Length > IpcProtocol.MaxPayloadBytes)
        {
            throw new IpcProtocolException(
                $"A frame payload must be 1 to {IpcProtocol.MaxPayloadBytes} bytes, not {payload.Length}.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(HeaderSize + payload.Length);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, payload.Length);
            payload.CopyTo(buffer.AsMemory(HeaderSize));
            await stream.WriteAsync(buffer.AsMemory(0, HeaderSize + payload.Length), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <returns>The payload, or null on a clean end of stream at a frame boundary.</returns>
    /// <exception cref="IpcProtocolException">A bad length, or the stream ends inside a frame.</exception>
    public static ValueTask<byte[]?> ReadAsync(Stream stream, CancellationToken ct) =>
        ReadAsync(stream, IpcProtocol.MaxPayloadBytes, ct);

    /// <summary>
    /// Reads one frame whose payload may be at most <paramref name="maxPayload"/> bytes; a longer declared length is a
    /// protocol error raised before anything is allocated for the payload.
    /// </summary>
    /// <returns>The payload, or null on a clean end of stream at a frame boundary.</returns>
    /// <exception cref="IpcProtocolException">A bad length, or the stream ends inside a frame.</exception>
    public static async ValueTask<byte[]?> ReadAsync(Stream stream, int maxPayload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPayload, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPayload, IpcProtocol.MaxPayloadBytes);

        byte[] header = new byte[HeaderSize];
        int read = await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < HeaderSize)
        {
            throw new IpcProtocolException("The stream ended inside a frame header.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxPayload)
        {
            throw new IpcProtocolException(
                $"A frame length must be 1 to {maxPayload}, not {length}.");
        }

        byte[] payload = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            throw new IpcProtocolException("The stream ended inside a frame body.");
        }

        return payload;
    }
}
