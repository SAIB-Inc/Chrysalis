using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Chrysalis.Network.Core;
using System.IO;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Demultiplexes incoming data from the bearer connection and routes it to the appropriate agent channels.
/// </summary>
/// <param name="bearer">The bearer connection to receive data from.</param>
public class Demuxer(IBearer bearer) : IDisposable
{
    private readonly IBearer _bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
    private readonly Dictionary<ProtocolType, Subject<byte[]>> _egressChannels = new();
    private readonly CancellationTokenSource _demuxerCts = new();

    /// <summary>
    /// Reads a full segment from the bearer, including header and payload, handling partial reads.
    /// Waits indefinitely if no bytes are available until data arrives or the operation is canceled.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A tuple containing the protocol type and payload of the segment.</returns>
    public async Task<(ProtocolType protocol, byte[] payload)> ReadSegmentAsync(CancellationToken cancellationToken)
    {
        const int HeaderLength = 8;
        byte[] headerBytes = await ReadExactlyAsync(HeaderLength, cancellationToken);
        MuxSegment headerSegment = MuxSegmentCodec.DecodeHeader(headerBytes);
        byte[] payloadBytes = await ReadExactlyAsync(headerSegment.PayloadLength, cancellationToken);
        return (headerSegment.ProtocolId, payloadBytes);
    }

    /// <summary>
    /// Reads exactly n bytes from the bearer, using a MemoryStream buffer to handle partial reads.
    /// Waits indefinitely if no bytes are available until data arrives or the operation is canceled.
    /// </summary>
    /// <param name="n">The number of bytes to read.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>An array containing exactly n bytes.</returns>
    private async Task<byte[]> ReadExactlyAsync(int n, CancellationToken cancellationToken)
    {
        byte[] result = new byte[n];
        int bytesRead = 0;

        // Buffer scoped to this method
        using (MemoryStream buffer = new())
        {
            while (bytesRead < n)
            {
                int bytesToRead = n - bytesRead;
                int readFromBuffer = buffer.Read(result, bytesRead, bytesToRead);

                if (readFromBuffer > 0)
                {
                    bytesRead += readFromBuffer;
                }
                else
                {
                    byte[] received = await _bearer.ReceiveAsync(cancellationToken);
                    if (received.Length > 0)
                    {
                        buffer.Write(received, 0, received.Length);
                        buffer.Position = 0; 
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Subscribes to receive messages for a specific protocol type.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>The Subject for the protocol, to which payloads will be pushed.</returns>
    public Subject<byte[]> Subscribe(ProtocolType protocol)
    {
        if (!_egressChannels.TryGetValue(protocol, out var subject))
        {
            subject = new Subject<byte[]>();
            _egressChannels[protocol] = subject;
        }
        return subject;
    }

    /// <summary>
    /// Processes a single segment and forwards it to the appropriate channel.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var (protocol, payload) = await ReadSegmentAsync(cancellationToken);

        if (_egressChannels.TryGetValue(protocol, out var channelSubject))
        {
            channelSubject.OnNext(payload);
        }
    }

    /// <summary>
    /// Runs the demultiplexer in a continuous loop, processing incoming segments.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Run(async () => // Run the loop on a background thread using Task.Run for simplicity
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Disposes of the demuxer and releases resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _demuxerCts.Cancel();

        foreach (var subject in _egressChannels.Values)
        {
            subject.OnCompleted();
            subject.Dispose();
        }
        _egressChannels.Clear();
        _demuxerCts.Dispose();
    }
}