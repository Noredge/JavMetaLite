using System.IO;
using System.Net;
using System.Net.Http;

namespace JavMetaLite.RegressionTests;

// Shared by the Windows regression suite and the portable Core tests.
internal sealed class GatedDownloadHandler(bool fatal) : HttpMessageHandler
{
    private readonly TaskCompletionSource _threeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cleanup = new();
    private int _pending, _calls, _canceled, _cancelableBody, _disposed;
    public int Pending => Volatile.Read(ref _pending);
    public int Calls => Volatile.Read(ref _calls);
    public int Canceled => Volatile.Read(ref _canceled);
    public bool SawCancelableBodyToken => Volatile.Read(ref _cancelableBody) != 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host != "downloads.invalid") throw new InvalidOperationException("No live requests allowed.");
        var number = Interlocked.Increment(ref _calls);
        // HttpContent's default ReadAsStreamAsync can buffer via a serializer
        // without forwarding the caller's cancellation token. Model an actual
        // network response stream, whose ReadAsync receives the body deadline.
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new GatedStream(this, number)) });
    }

    private async Task ReadBodyAsync(int number, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled) Interlocked.Exchange(ref _cancelableBody, 1);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cleanup.Token);
        if (Interlocked.Increment(ref _pending) == 3) _threeStarted.TrySetResult();
        try
        {
            await _threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), combined.Token);
            if (fatal && number == 1) throw new InvalidOperationException("Synthetic fatal body failure.");
            await Task.Delay(Timeout.Infinite, combined.Token);
        }
        catch (OperationCanceledException) { Interlocked.Increment(ref _canceled); throw; }
        finally { Interlocked.Decrement(ref _pending); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _cleanup.Cancel();
            _cleanup.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class GatedStream(GatedDownloadHandler owner, int number) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await owner.ReadBodyAsync(number, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
