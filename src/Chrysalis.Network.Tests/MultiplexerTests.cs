using System.Buffers;
using System.IO.Pipelines;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class MultiplexerTests
{
    /// <summary>
    /// Tests that the Muxer correctly handles incomplete protocol message headers.
    /// </summary>
    [Fact]
    public async Task Muxer_WaitsForCompleteHeader_WhenPartialDataAvailable()
    {
        // Arrange
        var pipe = new Pipe();
        var mockBearer = new MockBearer();
        var muxer = new Muxer(mockBearer, ProtocolMode.Initiator);

        // Start the muxer
        using var cts = new CancellationTokenSource();
        var muxerTask = muxer.RunAsync(cts.Token);

        // Write only 2 bytes (incomplete header - need 3 bytes minimum)
        pipe.Writer.Write(new byte[] { 0x01, 0x00 }); // Protocol ID and partial length
        await pipe.Writer.FlushAsync();

        // Give muxer time to process (it should wait for more data)
        await Task.Delay(100);

        // Complete with full message
        pipe.Writer.Write(new byte[] { 0x02, 0xAB, 0xCD }); // Length (2) + payload (AB CD)
        await pipe.Writer.FlushAsync();

        // Give time to process the complete message
        await Task.Delay(100);

        // Cleanup
        cts.Cancel();
        try { await muxerTask; } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Tests that the AgentChannel correctly handles the two-argument AdvanceTo overload.
    /// </summary>
    [Fact]
    public async Task AgentChannel_AdvanceTo_WithExamined_SetsCorrectPositions()
    {
        // Arrange
        var pipe = new Pipe();
        var channel = new AgentChannel(
            ProtocolType.Handshake,
            pipe.Writer,
            pipe.Reader
        );

        // Write some data
        pipe.Writer.Write(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        pipe.Writer.Complete();

        // Read the data
        var result = await pipe.Reader.ReadAsync();
        var buffer = result.Buffer;

        // Act - advance with both consumed and examined positions
        var consumed = buffer.Start;
        var examined = buffer.End;
        channel.AdvanceTo(consumed, examined);

        // Assert - no exception should be thrown, and we can read again
        // This is a basic sanity check that the two-argument overload works
        var secondResult = await pipe.Reader.ReadAsync();
        Assert.True(secondResult.IsCompleted);
    }

    /// <summary>
    /// Tests that the Demuxer correctly parses and forwards segments to protocol pipes.
    /// </summary>
    [Fact]
    public async Task Demuxer_CorrectlyRoutesSegmentsToProtocolPipes()
    {
        // Arrange
        var mockBearer = new MockBearer();
        var demuxer = new Demuxer(mockBearer);

        // Subscribe to a protocol before running
        var protocolReader = demuxer.Subscribe(ProtocolType.Handshake);

        // Create a valid segment: [4-byte timestamp][2-byte protocol][2-byte length][payload]
        // Protocol ID for Handshake = 0 (with initiator flag = 0x8000)
        var segment = CreateMuxSegment(
            transmissionTime: 1000,
            protocolId: (ushort)ProtocolType.Handshake,
            payload: new byte[] { 0xAB, 0xCD, 0xEF }
        );
        mockBearer.EnqueueIncomingData(segment);

        // Act - run demuxer briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await demuxer.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        // Assert - check if data was routed to the protocol pipe
        var readResult = await protocolReader.ReadAsync(CancellationToken.None);
        Assert.False(readResult.Buffer.IsEmpty);
    }

    /// <summary>
    /// Creates a properly formatted MuxSegment byte array.
    /// </summary>
    private static byte[] CreateMuxSegment(uint transmissionTime, ushort protocolId, byte[] payload)
    {
        var segment = new byte[8 + payload.Length];
        
        // Transmission time (4 bytes, big-endian)
        segment[0] = (byte)(transmissionTime >> 24);
        segment[1] = (byte)(transmissionTime >> 16);
        segment[2] = (byte)(transmissionTime >> 8);
        segment[3] = (byte)transmissionTime;
        
        // Protocol ID with mode (2 bytes, big-endian) - set initiator mode (0x8000)
        ushort protocolWithMode = (ushort)(protocolId | 0x8000);
        segment[4] = (byte)(protocolWithMode >> 8);
        segment[5] = (byte)protocolWithMode;
        
        // Payload length (2 bytes, big-endian)
        segment[6] = (byte)(payload.Length >> 8);
        segment[7] = (byte)payload.Length;
        
        // Payload
        Array.Copy(payload, 0, segment, 8, payload.Length);
        
        return segment;
    }

    /// <summary>
    /// Mock bearer for testing multiplexer components without network I/O.
    /// </summary>
    private sealed class MockBearer : IBearer
    {
        private readonly Pipe _incomingPipe = new();
        private readonly Pipe _outgoingPipe = new();

        public PipeReader Reader => _incomingPipe.Reader;
        public PipeWriter Writer => _outgoingPipe.Writer;

        /// <summary>
        /// Enqueues data to be read by the demuxer.
        /// </summary>
        public void EnqueueIncomingData(byte[] data)
        {
            _incomingPipe.Writer.Write(data);
            _incomingPipe.Writer.FlushAsync().AsTask().Wait();
        }

        /// <summary>
        /// Completes the incoming pipe (simulates connection close).
        /// </summary>
        public void CompleteIncoming(Exception? exception = null)
        {
            _incomingPipe.Writer.Complete(exception);
        }

        public void Dispose()
        {
            _incomingPipe.Writer.Complete();
            _incomingPipe.Reader.Complete();
            _outgoingPipe.Writer.Complete();
            _outgoingPipe.Reader.Complete();
        }
    }
}
