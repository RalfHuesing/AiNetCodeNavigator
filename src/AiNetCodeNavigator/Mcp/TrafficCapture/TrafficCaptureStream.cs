using System.Buffers;

namespace AiNetCodeNavigator.Mcp.TrafficCapture;

/// <summary>Taps newline-delimited JSON frames while preserving the underlying byte stream.</summary>
internal sealed class TrafficCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<ReadOnlyMemory<byte>> _onFrame;
    private readonly Action? _onFrameTooLarge;
    private readonly long _maxFrameBytes;
    private readonly bool _leaveOpen;
    private readonly ArrayBufferWriter<byte> _frame = new();
    private bool _captureFrame = true;

    internal TrafficCaptureStream(
        Stream inner,
        Action<ReadOnlyMemory<byte>> onFrame,
        long maxFrameBytes,
        bool leaveOpen = true,
        Action? onFrameTooLarge = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(onFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        _inner = inner;
        _onFrame = onFrame;
        _onFrameTooLarge = onFrameTooLarge;
        _maxFrameBytes = maxFrameBytes;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Observe(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        Observe(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        Observe(buffer.Span[..read]);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        Observe(buffer.AsSpan(offset, read));
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Observe(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        Observe(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Observe(buffer.Span);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Observe(buffer.AsSpan(offset, count));
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    private void Observe(ReadOnlySpan<byte> bytes)
    {
        if (!_captureFrame || bytes.IsEmpty)
        {
            return;
        }

        foreach (var value in bytes)
        {
            if (_frame.WrittenCount >= _maxFrameBytes)
            {
                _captureFrame = false;
                _frame.Clear();
                try { _onFrameTooLarge?.Invoke(); }
                catch { /* Capture is diagnostic and must never interfere with MCP traffic. */ }
                return;
            }

            var destination = _frame.GetSpan(1);
            destination[0] = value;
            _frame.Advance(1);
            if (value == (byte)'\n')
            {
                try
                {
                    _onFrame(_frame.WrittenMemory);
                }
                catch
                {
                    // Capture is diagnostic and must never interfere with MCP traffic.
                }

                _frame.Clear();
            }
        }
    }
}
