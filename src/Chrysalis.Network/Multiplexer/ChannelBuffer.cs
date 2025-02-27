using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.Multiplexer;
/// <summary>
/// A channel abstraction to hide the complexity of partial message payloads.
/// Wraps an AgentChannel to provide message-based communication over chunk-based transport.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ChannelBuffer class.
/// </remarks>
/// <param name="channel">The agent channel to wrap.</param>
public class ChannelBuffer(AgentChannel channel) : IDisposable
{
    private readonly AgentChannel _channel = channel;
    private readonly MemoryStream _buffer = new();
    
    // Maximum size of a single payload chunk, matching the Rust implementation
    private const int MAX_SEGMENT_PAYLOAD_LENGTH = 65535;
    
    // Error definitions
    private static readonly Error DecodingError = Error.New("Failed to decode message");
    private static readonly Error BufferError = Error.New("Buffer operation failed");

    // Helper function to wrap a potentially throwing operation in an Aff
    private static Aff<A> TryAff<A>(Func<A> f, Func<Exception, Error> onFail) =>
        Try(() => f())
        .Match(
            Succ: val => SuccessAff(val),
            Fail: ex => FailAff<A>(onFail(ex))
        );

    /// <summary>
    /// Sends a message as a sequence of payload chunks.
    /// </summary>
    /// <typeparam name="T">The type of message to send.</typeparam>
    /// <param name="msg">The message to send.</param>
    /// <returns>An Aff monad yielding Unit upon completion.</returns>
    public Aff<Unit> SendMsgChunks<T>(T msg) where T : CborBase =>
        from payload in Aff(() => ValueTask.FromResult(CborSerializer.Serialize(msg)))
        from _ in SendPayloadInChunks(payload)
        select unit;
    
    /// <summary>
    /// Sends a payload in chunks that fit within the maximum segment size.
    /// </summary>
    /// <param name="payload">The complete payload to send in chunks.</param>
    /// <returns>An Aff monad yielding Unit upon completion.</returns>
    private Aff<Unit> SendPayloadInChunks(byte[] payload)
    {
        List<byte[]> chunks = SplitPayloadIntoChunks(payload);
        
        // Build a sequential composition of send effects using fold
        return chunks.Fold(
            SuccessAff(unit),
            (aff, chunk) => 
                from _ in aff
                from __ in _channel.EnqueueChunk(chunk)
                select unit
        );
    }

    /// <summary>
    /// Reads from the channel until a complete message is received.
    /// </summary>
    /// <typeparam name="T">The type of message to receive.</typeparam>
    /// <returns>An Aff monad yielding the complete received message.</returns>
    public Aff<T> RecvFullMsg<T>() where T : CborBase =>
        from maybeMessage in CheckBufferForCompleteMessage<T>()
        from result in maybeMessage.Match(
            Some: msg => SuccessAff(msg),
            None: () => ReceiveUntilCompleteMessage<T>()
        )
        select result;
    
    /// <summary>
    /// Checks if the buffer already contains a complete message.
    /// </summary>
    /// <typeparam name="T">The type of message to check for.</typeparam>
    /// <returns>An Aff monad yielding Some(message) if a complete message is in the buffer, or None otherwise.</returns>
    private Aff<Option<T>> CheckBufferForCompleteMessage<T>() where T : CborBase =>
        TryAff(
            () => _buffer.Length > 0 ? TryDecodeMessage<T>() : Option<T>.None,
            ex => BufferError
        );
    
    /// <summary>
    /// Receives chunks until a complete message can be decoded.
    /// </summary>
    /// <typeparam name="T">The type of message to receive.</typeparam>
    /// <returns>An Aff monad yielding the complete decoded message.</returns>
    private Aff<T> ReceiveUntilCompleteMessage<T>() where T : CborBase =>
        TryReceiveLoop<T>().Bind(either => either.Match(
            Right: message => SuccessAff(message),
            Left: error => FailAff<T>(error)
        ));
    
    /// <summary>
    /// Loop to receive chunks until a complete message is available.
    /// </summary>
    /// <typeparam name="T">The type of message to receive.</typeparam>
    /// <returns>An Aff monad yielding Either a message (Right) or an error (Left).</returns>
    private Aff<Either<Error, T>> TryReceiveLoop<T>() where T : CborBase =>
        // Using Aff.BindAsync to properly transform the asynchronous computation
        Aff<Either<Error, T>>(async () =>
        {
            try
            {
                // Loop until we get a complete message or encounter an error
                while (true)
                {
                    // Get the next chunk from the channel
                    Fin<byte[]> chunkResult = await _channel.ReceiveNextChunk().Run();

                    // Process the result with pattern matching
                    byte[] chunk = chunkResult.Match(
                        Succ: data => data,
                        Fail: ex => throw ex // Will be caught in the try-catch
                    );

                    // Append the chunk to our buffer
                    AppendToBuffer(chunk);

                    // Try to decode a message from the buffer
                    Option<T> message = TryDecodeMessage<T>();
                    if (message.IsSome)
                    {
                        return message.Map(m => Right<Error, T>(m))
                                     .IfNone(() => Left<Error, T>(DecodingError));
                    }
                    // Continue loop if no complete message yet
                }
            }
            catch (Exception)
            {
                return Left<Error, T>(DecodingError);
            }
        });

    /// <summary>
    /// Appends data to the buffer.
    /// </summary>
    /// <param name="data">The data to append.</param>
    private void AppendToBuffer(byte[] data)
    {
        long position = _buffer.Position;
        _buffer.Position = _buffer.Length;
        _buffer.Write(data, 0, data.Length);
        _buffer.Position = position;
    }

    /// <summary>
    /// Splits a payload into chunks of maximum size.
    /// </summary>
    /// <param name="payload">The payload to split.</param>
    /// <returns>A list of payload chunks.</returns>
    private static List<byte[]> SplitPayloadIntoChunks(byte[] payload)
    {
        List<byte[]> chunks = [];
        
        for (int offset = 0; offset < payload.Length; offset += MAX_SEGMENT_PAYLOAD_LENGTH)
        {
            int chunkSize = System.Math.Min(MAX_SEGMENT_PAYLOAD_LENGTH, payload.Length - offset);
            byte[] chunk = new byte[chunkSize];
            System.Array.Copy(payload, offset, chunk, 0, chunkSize);
            chunks.Add(chunk);
        }
        
        return chunks;
    }

    /// <summary>
    /// Attempts to decode a complete message from the buffer.
    /// If successful, removes the decoded portion from the buffer.
    /// </summary>
    /// <typeparam name="T">The type of message to decode.</typeparam>
    /// <returns>An Option containing the decoded message, or None if decoding failed.</returns>
    private Option<T> TryDecodeMessage<T>() where T : CborBase =>
        Try(() =>
            {
                // Save the current position
                long originalPosition = _buffer.Position;

                // Reset to the beginning for reading
                _buffer.Position = 0;

                // Get the buffer content as a byte array
                byte[] data = _buffer.ToArray();

                // Try to deserialize
                T message = CborSerializer.Deserialize<T>(data);

                // If successful, remove the decoded portion from the buffer
                _buffer.SetLength(0);

                return message;
            }
        ).ToOption();

    /// <summary>
    /// Disposes the ChannelBuffer resources.
    /// </summary>
    public void Dispose()
    {
        _buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}