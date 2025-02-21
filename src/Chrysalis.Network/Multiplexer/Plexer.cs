namespace Chrysalis.Network.Multiplexer;

public class Plexer(IBearer bearer) : IDisposable
{
    private Muxer Muxer { get; init; } = new(bearer);
    private Demuxer Demuxer { get; init; } = new(bearer);
    private CancellationTokenSource TokenSource { get; init; } = new();

    public void Spawn()
    {
        Task.WhenAll(
            Muxer.RunAsync(TokenSource.Token), 
            Demuxer.RunAsync(TokenSource.Token)
        );
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TokenSource.Cancel();
        TokenSource.Dispose();
    }
}