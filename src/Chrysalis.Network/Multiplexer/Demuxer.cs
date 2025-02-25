using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Subjects;
using System.Threading;
using LanguageExt;
using static LanguageExt.Prelude;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Demultiplexes incoming data from the bearer connection and routes it to the appropriate agent channels.
/// </summary>
/// <param name="bearer">The bearer connection to receive data from.</param>
public class Demuxer(IBearer bearer) : IDisposable
{
    // Primary constructor parameter 'bearer' becomes the readonly field.
    private readonly IBearer _bearer = bearer;
    private readonly Dictionary<ProtocolType, Subject<byte[]>> _egressChannels = [];
    private readonly CancellationTokenSource _demuxerCts = new();

    /// <summary>
    /// Reads a full segment from the bearer, including header and payload.
    /// It waits indefinitely if no bytes are available until data arrives or the operation is canceled.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding a tuple containing the protocol type and payload of the segment.
    /// </returns>
    public Aff<(ProtocolType protocol, byte[] payload)> ReadSegment(CancellationToken cancellationToken) =>
        from headerBytes in ReadExactly(8, cancellationToken)
        let headerSegment = MuxSegmentCodec.DecodeHeader(headerBytes)
        from payloadBytes in ReadExactly(headerSegment.PayloadLength, cancellationToken)
        select (headerSegment.ProtocolId, payloadBytes);

    /// <summary>
    /// Reads exactly n bytes from the bearer as a functional effect.
    /// Uses a MemoryStream buffer to handle partial reads.
    /// </summary>
    /// <param name="n">The number of bytes to read.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding an array containing exactly n bytes.
    /// </returns>
    private Aff<byte[]> ReadExactly(int n, CancellationToken cancellationToken) =>
        Aff(async () =>
        {
            byte[] result = new byte[n];
            int bytesRead = 0;
            // Use a MemoryStream as a temporary buffer.
            using var buffer = new MemoryStream();
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
                    // Run the Aff effect and explicitly cast the Fin<byte[]> to byte[].
                    byte[] received = (byte[])await _bearer.ReceiveExact(n, cancellationToken).Run();
                    if (received.Length > 0)
                    {
                        buffer.Write(received, 0, received.Length);
                        // Reset the buffer position to allow reading the newly written bytes.
                        buffer.Position = 0;
                    }
                }
            }
            return result;
        });

    /// <summary>
    /// Subscribes to receive messages for a specific protocol type.
    /// </summary>
    /// <param name="protocol">The protocol type to subscribe to.</param>
    /// <returns>
    /// The Subject for the protocol, to which payloads will be pushed.
    /// </returns>
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
    /// <returns>
    /// An Aff monad yielding Unit upon processing the segment.
    /// </returns>
    public Aff<Unit> Tick(CancellationToken cancellationToken) =>
        // Read a segment and then push its payload to the corresponding channel.
        ReadSegment(cancellationToken).Map(segment =>
        {
            if (_egressChannels.TryGetValue(segment.protocol, out var channelSubject))
            {
                channelSubject.OnNext(segment.payload);
            }
            return unit;
        });

    /// <summary>
    /// Runs the demultiplexer in a continuous loop as a functional effect.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>
    /// An Aff monad yielding Unit when processing completes or cancellation is triggered.
    /// </returns>
    public Aff<Unit> Run(CancellationToken cancellationToken) =>
        Aff(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Tick(cancellationToken).Run();
            }
            return unit;
        });

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
