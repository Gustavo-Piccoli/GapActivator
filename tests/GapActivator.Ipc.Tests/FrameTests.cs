using System.Buffers.Binary;
using GapActivator.Ipc.Tests.Support;

namespace GapActivator.Ipc.Tests;

public class FrameTests
{
    private static byte[] Header(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        return header;
    }

    private static async Task<byte[]> FrameOfAsync(byte[] payload)
    {
        using var stream = new MemoryStream();
        await IpcFrame.WriteAsync(stream, payload, CancellationToken.None);
        return stream.ToArray();
    }

    [Fact]
    public async Task Frame_WriteThenRead_RoundTripsThePayload()
    {
        byte[] payload = "{\"a\":1}"u8.ToArray();
        using var stream = new MemoryStream();

        await IpcFrame.WriteAsync(stream, payload, CancellationToken.None);
        stream.ToArray().Take(4).ToArray().ShouldBe(Header(payload.Length));
        stream.Position = 0;

        byte[]? read = await IpcFrame.ReadAsync(stream, CancellationToken.None);

        read.ShouldBe(payload);
    }

    [Fact]
    public async Task Frame_ReadFromOneByteChunks_ReassemblesTheFrame()
    {
        byte[] payload = "{\"hello\":\"world\"}"u8.ToArray();
        using var stream = new ChunkedStream(await FrameOfAsync(payload));

        byte[]? read = await IpcFrame.ReadAsync(stream, CancellationToken.None);

        read.ShouldBe(payload);
    }

    [Fact]
    public async Task Frame_TwoFramesBackToBack_AreReadInOrder()
    {
        byte[] first = "{\"n\":1}"u8.ToArray();
        byte[] second = "{\"n\":22}"u8.ToArray();
        using var stream = new MemoryStream();
        await IpcFrame.WriteAsync(stream, first, CancellationToken.None);
        await IpcFrame.WriteAsync(stream, second, CancellationToken.None);
        stream.Position = 0;

        (await IpcFrame.ReadAsync(stream, CancellationToken.None)).ShouldBe(first);
        (await IpcFrame.ReadAsync(stream, CancellationToken.None)).ShouldBe(second);
        (await IpcFrame.ReadAsync(stream, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Frame_EndOfStreamAtABoundary_ReturnsNull()
    {
        using var stream = new MemoryStream();

        (await IpcFrame.ReadAsync(stream, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(4, 5)]
    public async Task Frame_EndOfStreamInsideHeaderOrBody_ThrowsProtocolException(int headerBytes, int bodyBytes)
    {
        byte[] content = headerBytes == 4
            ? [.. Header(10), .. new byte[bodyBytes]]
            : [.. Header(10).Take(headerBytes)];
        using var stream = new MemoryStream(content);

        await Should.ThrowAsync<IpcProtocolException>(async () =>
            await IpcFrame.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_LengthAboveTheMaximum_ThrowsWithoutReadingTheBody()
    {
        using var stream = new MemoryStream([.. Header((16 * 1024 * 1024) + 1), .. new byte[64]]);

        await Should.ThrowAsync<IpcProtocolException>(async () =>
            await IpcFrame.ReadAsync(stream, CancellationToken.None));

        stream.Position.ShouldBe(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Frame_ZeroOrNegativeLength_ThrowsProtocolException(int length)
    {
        using var stream = new MemoryStream([.. Header(length), .. new byte[8]]);

        await Should.ThrowAsync<IpcProtocolException>(async () =>
            await IpcFrame.ReadAsync(stream, CancellationToken.None));

        stream.Position.ShouldBe(4);
    }

    [Fact]
    public async Task Frame_PayloadOfExactlyTheMaximum_IsAccepted()
    {
        byte[] payload = new byte[IpcProtocol.MaxPayloadBytes];
        Array.Fill(payload, (byte)'x');
        using var stream = new MemoryStream();

        await IpcFrame.WriteAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        byte[]? read = await IpcFrame.ReadAsync(stream, CancellationToken.None);

        read.ShouldNotBeNull();
        read.Length.ShouldBe(IpcProtocol.MaxPayloadBytes);
    }

    [Fact]
    public async Task Frame_WritePayloadAboveTheMaximum_ThrowsAndWritesNothing()
    {
        using var stream = new MemoryStream();

        await Should.ThrowAsync<IpcProtocolException>(async () =>
            await IpcFrame.WriteAsync(stream, new byte[IpcProtocol.MaxPayloadBytes + 1], CancellationToken.None));

        stream.Length.ShouldBe(0);
    }

    [Fact]
    public async Task Frame_WriteEmptyPayload_ThrowsAndWritesNothing()
    {
        using var stream = new MemoryStream();

        await Should.ThrowAsync<IpcProtocolException>(async () =>
            await IpcFrame.WriteAsync(stream, ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        stream.Length.ShouldBe(0);
    }
}
