namespace GapActivator.Ipc.Tests.Support;

/// <summary>A read-only stream that hands out at most one byte per read, to exercise partial reads.</summary>
internal sealed class ChunkedStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, Math.Min(count, 1));

    public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, 1)]);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(_inner.Read(buffer.Span[..Math.Min(buffer.Length, 1)]));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(_inner.Read(buffer, offset, Math.Min(count, 1)));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
