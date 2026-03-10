using System.IO.Pipelines;
using System.Threading.Channels;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides bidirectional multiplexing capabilities over a single bearer.
/// </summary>
public sealed class Plexer : IDisposable
{
    private readonly IBearer _bearer;
    private readonly SemaphoreSlim _bearerWriteLock = new(1, 1);
    private readonly Demuxer _demuxer;
    private readonly Muxer _muxer;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plexer"/> class.
    /// </summary>
    /// <param name="bearer">The bearer for network communication.</param>
    public Plexer(IBearer bearer)
    {
        _bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
        _demuxer = new Demuxer(bearer);
        _muxer = new Muxer(bearer.Writer, ProtocolMode.Initiator, _bearerWriteLock);
    }

    /// <summary>
    /// Creates a channel for client-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeClient(ProtocolType protocol)
    {
        PipeWriter muxerWriter = _muxer.Writer;
        PipeWriter bearerWriter = _bearer.Writer;
        ChannelReader<ReadOnlyMemory<byte>> reader = _demuxer.Subscribe(protocol);
        return new AgentChannel(protocol, muxerWriter, bearerWriter, reader, _bearerWriteLock);
    }

    /// <summary>
    /// Creates a channel for server-side protocol communication.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>A bidirectional agent channel for the specified protocol.</returns>
    public AgentChannel SubscribeServer(ProtocolType protocol) => SubscribeClient(protocol);

    /// <summary>
    /// Starts the plexer, running both the demuxer and muxer.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task demuxerTask = _demuxer.RunAsync(linkedCts.Token);
        Task muxerTask = _muxer.RunAsync(linkedCts.Token);

        Task completedTask = await Task.WhenAny(demuxerTask, muxerTask).ConfigureAwait(false);

        await linkedCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(demuxerTask, muxerTask).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (TimeoutException)
        {
            // Timeout waiting for tasks to finish
        }

        string failedComponent = completedTask == demuxerTask ? "Demuxer" : "Muxer";
        await completedTask.ConfigureAwait(false);
        throw new InvalidOperationException($"{failedComponent} completed unexpectedly without error");
    }

    /// <summary>
    /// Disposes the resources used by the plexer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _muxer.Dispose();
        _demuxer.Dispose();
        _bearer.Dispose();
        _bearerWriteLock.Dispose();

        _isDisposed = true;
    }
}
