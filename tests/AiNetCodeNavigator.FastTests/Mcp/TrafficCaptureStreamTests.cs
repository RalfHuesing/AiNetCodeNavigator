using System.Text;
using AiNetCodeNavigator.Mcp.TrafficCapture;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class TrafficCaptureStreamTests
{
    [Fact]
    public async Task ReadTapsCompleteFramesAcrossPartialReadsWithoutChangingWireBytes()
    {
        var wire = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1}\r\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n");
        var captured = new List<byte[]>();
        await using var inner = new ChunkedReadStream(wire, maxRead: 3);
        await using var stream = new TrafficCaptureStream(inner, frame => captured.Add(frame.ToArray()), maxFrameBytes: 4096);
        using var received = new MemoryStream();
        var buffer = new byte[5];

        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            await received.WriteAsync(buffer.AsMemory(0, count));
        }

        Assert.Equal(wire, received.ToArray());
        Assert.Equal(
            new[]
            {
                Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1}\r\n"),
                Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n"),
            },
            captured);
    }

    [Fact]
    public async Task WriteTapsExactFramesAndCallbackFailureDoesNotBreakTransport()
    {
        var wire = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":4,\"result\":{}}\n");
        await using var inner = new MemoryStream();
        await using var stream = new TrafficCaptureStream(inner, _ => throw new IOException("capture unavailable"), maxFrameBytes: 4096);

        await stream.WriteAsync(wire);
        await stream.FlushAsync();

        Assert.Equal(wire, inner.ToArray());
    }

    [Fact]
    public async Task SeparateWritesAreCombinedIntoTheOriginalNewlineDelimitedFrame()
    {
        var captured = new List<byte[]>();
        await using var inner = new MemoryStream();
        await using var stream = new TrafficCaptureStream(inner, frame => captured.Add(frame.ToArray()), maxFrameBytes: 4096);

        await stream.WriteAsync("{\"jsonrpc\":"u8.ToArray());
        await stream.WriteAsync("\"2.0\",\"id\":9}\n"u8.ToArray());

        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":9}\n"u8.ToArray(), inner.ToArray());
        Assert.Equal(new[] { "{\"jsonrpc\":\"2.0\",\"id\":9}\n"u8.ToArray() }, captured);
    }

    [Fact]
    public async Task IncompleteTrailingFrameIsPassedThroughAndNotReportedAsAFrame()
    {
        var wire = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1}\npartial");
        var captured = new List<byte[]>();
        await using var inner = new ChunkedReadStream(wire, maxRead: 2);
        await using var stream = new TrafficCaptureStream(inner, frame => captured.Add(frame.ToArray()), maxFrameBytes: 4096);
        using var received = new MemoryStream();
        var buffer = new byte[7];

        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            await received.WriteAsync(buffer.AsMemory(0, count));
        }

        Assert.Equal(wire, received.ToArray());
        Assert.Equal(new[] { Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1}\n") }, captured);
    }

    [Fact]
    public async Task OversizedFrameDisablesCaptureButDoesNotTruncateTransport()
    {
        var wire = Encoding.UTF8.GetBytes("123456789\n{\"jsonrpc\":\"2.0\",\"id\":2}\n");
        var captured = new List<byte[]>();
        await using var inner = new ChunkedReadStream(wire, maxRead: 4);
        await using var stream = new TrafficCaptureStream(inner, frame => captured.Add(frame.ToArray()), maxFrameBytes: 8);
        using var received = new MemoryStream();
        var buffer = new byte[6];

        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            await received.WriteAsync(buffer.AsMemory(0, count));
        }

        Assert.Equal(wire, received.ToArray());
        Assert.Empty(captured);
    }

    [Fact]
    public async Task CancellationInterruptsAnUnderlyingReadThatIgnoresItsToken()
    {
        await using var inner = new PendingReadStream();
        await using var stream = new TrafficCaptureStream(inner, _ => { }, maxFrameBytes: 4096);
        using var cancellation = new CancellationTokenSource();
        var read = stream.ReadAsync(new byte[1], cancellation.Token).AsTask();

        try
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            inner.ReleasePendingRead();
        }
    }

    private sealed class ChunkedReadStream(byte[] content, int maxRead) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = Math.Min(Math.Min(count, maxRead), content.Length - _offset);
            content.AsSpan(_offset, available).CopyTo(buffer.AsSpan(offset, available));
            _offset += available;
            return available;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var available = Math.Min(Math.Min(buffer.Length, maxRead), content.Length - _offset);
            content.AsMemory(_offset, available).CopyTo(buffer);
            _offset += available;
            return ValueTask.FromResult(available);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PendingReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _pendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(_pendingRead.Task);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        internal void ReleasePendingRead() => _pendingRead.TrySetResult(0);
    }
}
