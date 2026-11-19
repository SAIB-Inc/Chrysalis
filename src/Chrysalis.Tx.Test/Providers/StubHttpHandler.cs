using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Chrysalis.Tx.Test.Providers;

internal sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    internal ConcurrentQueue<(HttpMethod Method, string Uri)> Requests { get; } = new();

    internal StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : this((request, _) => Task.FromResult(answer(request))) { }

    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue((request.Method, request.RequestUri!.PathAndQuery));
        return answer(request, cancellationToken);
    }
}

internal sealed class StubBody(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
{
    internal int BytesRead { get; private set; }
    internal int LargestReadEnd { get; private set; }
    internal bool WasDisposed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        LargestReadEnd = Math.Max(LargestReadEnd, BytesRead + buffer.Length);
        int count = await read(buffer, cancellationToken);
        BytesRead += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }

    internal static StubBody Pending(TaskCompletionSource entered) => new(async (_, ct) =>
    {
        entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return 0;
    });

    internal static StubBody BreaksAfterPrefix()
    {
        bool first = true;
        return new((buffer, _) =>
        {
            if (!first) { throw new IOException("CANARY-broken-body"); }
            first = false;
            buffer.Span[0] = (byte)'[';
            return ValueTask.FromResult(1);
        });
    }
}
