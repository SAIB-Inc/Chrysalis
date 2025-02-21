namespace Chrysalis.Network.Multiplexer;

public class Demuxer(IBearer bearer)
{
    private readonly IBearer _bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
    private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> _egressChannels = [];

    // Read a full segment from the bearer, including header and payload
    public async Task<(ushort protocol, byte[] payload)> ReadSegmentAsync(CancellationToken cancellationToken)
    {
        const int HEADER_LEN = 8;
        var headerBuffer = new byte[HEADER_LEN];
        await _bearer.ReceiveAsync(cancellationToken).ConfigureAwait(false); // Receive header

        throw new NotImplementedException();
    }

    // Method to demux (process) the segment based on the protocol and payload
    private async Task DemuxAsync(ushort protocol, byte[] payload, CancellationToken cancellationToken)
    {
        // if (_egressChannels.ContainsKey(protocol))
        // {
        //     var egressChannel = _egressChannels[protocol];
        //     await egressChannel.SetResultAsync(payload, cancellationToken);
        // }
        // else
        // {
        //     Console.WriteLine($"Warning: Message for unregistered protocol {protocol}");
        // }
    }

    // Subscribe to receive messages for a specific protocol
    public TaskCompletionSource<byte[]> Subscribe(ushort protocol)
    {
        var tcs = new TaskCompletionSource<byte[]>();
        _egressChannels[protocol] = tcs;
        return tcs;
    }

    // Process a segment and forward it to the appropriate channel
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var (protocol, payload) = await ReadSegmentAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Demuxing protocol {protocol}");
        await DemuxAsync(protocol, payload, cancellationToken).ConfigureAwait(false);
    }

    // Run the demuxer in a continuous loop
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await TickAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}