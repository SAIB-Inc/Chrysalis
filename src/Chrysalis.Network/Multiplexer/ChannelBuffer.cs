using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides buffer management for sending and receiving complete CBOR messages over an AgentChannel.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ChannelBuffer class.
/// </remarks>
/// <param name="channel">The channel to buffer data for.</param>
public sealed class ChannelBuffer(AgentChannel channel)
{
    private static readonly ArrayPool<byte> ReceiveBufferPool = ArrayPool<byte>.Shared;

    private const byte AdditionalInfoOneByte = 24;
    private const byte AdditionalInfoTwoBytes = 25;
    private const byte AdditionalInfoFourBytes = 26;
    private const byte AdditionalInfoEightBytes = 27;
    private const byte AdditionalInfoIndefiniteLength = 31;

    private const byte MajorTypeByteString = 2;
    private const byte MajorTypeTextString = 3;
    private const byte MajorTypeArray = 4;
    private const byte MajorTypeMap = 5;
    private const byte MajorTypeTag = 6;
    private const byte MajorTypeSimple = 7;

    private const byte InitialByteAdditionalInfoMask = 0x1F;
    private const byte BreakStopCode = 0xFF;

    private enum ParseStatus
    {
        Complete,
        NeedMoreData,
        InvalidData
    }

    private enum ValueParseResult
    {
        Completed,
        NeedMoreData,
        InvalidData,
        StartContainer
    }

    private enum LengthReadStatus
    {
        Success,
        NeedMoreData,
        InvalidData
    }

    private enum FrameKind : byte
    {
        Root,
        DefiniteItemCount,
        IndefiniteArray,
        IndefiniteMap,
        IndefiniteByteString,
        IndefiniteTextString
    }

    private struct Frame
    {
        public FrameKind Kind;
        public ulong RemainingItems;
        public bool IndefiniteMapExpectingKey;

        public static Frame CreateRoot()
        {
            return new Frame
            {
                Kind = FrameKind.Root,
                RemainingItems = 1,
                IndefiniteMapExpectingKey = true
            };
        }

        public static Frame CreateDefinite(ulong remainingItems)
        {
            return new Frame
            {
                Kind = FrameKind.DefiniteItemCount,
                RemainingItems = remainingItems,
                IndefiniteMapExpectingKey = true
            };
        }

        public static Frame CreateIndefiniteArray()
        {
            return new Frame
            {
                Kind = FrameKind.IndefiniteArray,
                RemainingItems = 0,
                IndefiniteMapExpectingKey = true
            };
        }

        public static Frame CreateIndefiniteMap()
        {
            return new Frame
            {
                Kind = FrameKind.IndefiniteMap,
                RemainingItems = 0,
                IndefiniteMapExpectingKey = true
            };
        }

        public static Frame CreateIndefiniteByteString()
        {
            return new Frame
            {
                Kind = FrameKind.IndefiniteByteString,
                RemainingItems = 0,
                IndefiniteMapExpectingKey = true
            };
        }

        public static Frame CreateIndefiniteTextString()
        {
            return new Frame
            {
                Kind = FrameKind.IndefiniteTextString,
                RemainingItems = 0,
                IndefiniteMapExpectingKey = true
            };
        }
    }

    public async Task SendFullMessageAsync<T>(T message, CancellationToken cancellationToken) where T : CborBase
    {
        ReadOnlyMemory<byte> payloadMemory = CborSerializer.SerializeToMemory(message);
        int payloadLength = payloadMemory.Length;

        for (int offset = 0; offset < payloadLength; offset += ProtocolConstants.MaxSegmentPayloadLength)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            int chunkSize = Math.Min(ProtocolConstants.MaxSegmentPayloadLength, payloadLength - offset);
            ReadOnlyMemory<byte> chunkMemory = payloadMemory.Slice(offset, chunkSize);
            ReadOnlySequence<byte> chunkSequence = new(chunkMemory);
            await channel.EnqueueChunkAsync(chunkSequence, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<T> ReceiveFullMessageAsync<T>(CancellationToken cancellationToken) where T : CborBase
    {
        while (true)
        {
            ReadResult readResult = await channel.ReadChunkAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = readResult.Buffer;

            // Check if pipe was completed (demuxer stopped) before we have a complete message
            if (readResult.IsCompleted && buffer.Length == 0)
            {
                throw new InvalidOperationException(
                    "Connection closed by multiplexer. The demuxer has stopped, likely due to a network error or node disconnection. " +
                    "Check the plexer health status for the underlying exception."
                );
            }

            ParseStatus parseStatus = TryGetFirstValueLength(buffer, out int messageLength);

            if (parseStatus == ParseStatus.NeedMoreData)
            {
                if (readResult.IsCompleted)
                {
                    channel.AdvanceTo(buffer.End);
                    throw new InvalidOperationException(
                        "Pipe completed with partial data that could not be deserialized. " +
                        "This may indicate connection was closed mid-message."
                    );
                }

                channel.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            if (parseStatus == ParseStatus.InvalidData)
            {
                channel.AdvanceTo(buffer.End);
                throw new InvalidOperationException(
                    "Pipe delivered invalid CBOR data. " +
                    "This indicates a framing mismatch or corrupted payload."
                );
            }

            T result;

            if (buffer.IsSingleSegment)
            {
                ReadOnlyMemory<byte> messageBuffer = buffer.First[..messageLength];
                result = CborSerializer.Deserialize<T>(messageBuffer);
            }

            else
            {
                byte[] rentedBuffer = ReceiveBufferPool.Rent(messageLength);
                try
                {
                    buffer.Slice(0, messageLength).CopyTo(rentedBuffer.AsSpan(0, messageLength));
                    result = CborSerializer.Deserialize<T>(rentedBuffer.AsMemory(0, messageLength));
                }
                finally
                {
                    ReceiveBufferPool.Return(rentedBuffer);
                }
            }

            channel.AdvanceTo(buffer.GetPosition(messageLength));
            return result;
        }
    }

    private static ParseStatus TryGetFirstValueLength(ReadOnlySequence<byte> buffer, out int messageLength)
    {
        messageLength = 0;

        if (buffer.IsEmpty)
        {
            return ParseStatus.NeedMoreData;
        }

        SequenceReader<byte> reader = new(buffer);
        Span<Frame> frameStack = stackalloc Frame[64];
        int depth = 1;
        frameStack[0] = Frame.CreateRoot();

        while (depth > 0)
        {
            ref Frame currentFrame = ref frameStack[depth - 1];

            if (IsDefiniteFrame(currentFrame) && currentFrame.RemainingItems == 0)
            {
                depth--;

                if (depth == 0)
                {
                    if (reader.Consumed > int.MaxValue)
                    {
                        return ParseStatus.InvalidData;
                    }

                    messageLength = (int)reader.Consumed;
                    return ParseStatus.Complete;
                }

                MarkItemComplete(ref frameStack[depth - 1]);
                continue;
            }

            if (currentFrame.Kind is FrameKind.IndefiniteByteString or FrameKind.IndefiniteTextString)
            {
                if (!reader.TryPeek(out byte nextByte))
                {
                    return ParseStatus.NeedMoreData;
                }

                if (nextByte == BreakStopCode)
                {
                    reader.Advance(1);
                    depth--;

                    if (depth == 0)
                    {
                        return ParseStatus.InvalidData;
                    }

                    MarkItemComplete(ref frameStack[depth - 1]);
                    continue;
                }

                if (!reader.TryRead(out byte initialByte))
                {
                    return ParseStatus.NeedMoreData;
                }

                byte majorType = (byte)(initialByte >> 5);
                byte additionalInfo = (byte)(initialByte & InitialByteAdditionalInfoMask);
                byte expectedMajorType = currentFrame.Kind == FrameKind.IndefiniteByteString
                    ? MajorTypeByteString
                    : MajorTypeTextString;

                if (majorType != expectedMajorType || additionalInfo == AdditionalInfoIndefiniteLength)
                {
                    return ParseStatus.InvalidData;
                }

                LengthReadStatus lengthStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out ulong chunkLength);
                if (lengthStatus != LengthReadStatus.Success)
                {
                    return lengthStatus == LengthReadStatus.NeedMoreData
                        ? ParseStatus.NeedMoreData
                        : ParseStatus.InvalidData;
                }

                ParseStatus chunkStatus = TryAdvance(ref reader, chunkLength)
                    ? ParseStatus.Complete
                    : ParseStatus.NeedMoreData;
                if (chunkStatus != ParseStatus.Complete)
                {
                    return chunkStatus;
                }

                continue;
            }

            if (currentFrame.Kind is FrameKind.IndefiniteArray or FrameKind.IndefiniteMap)
            {
                if (!reader.TryPeek(out byte nextByte))
                {
                    return ParseStatus.NeedMoreData;
                }

                if (nextByte == BreakStopCode)
                {
                    if (currentFrame.Kind == FrameKind.IndefiniteMap && !currentFrame.IndefiniteMapExpectingKey)
                    {
                        return ParseStatus.InvalidData;
                    }

                    reader.Advance(1);
                    depth--;

                    if (depth == 0)
                    {
                        return ParseStatus.InvalidData;
                    }

                    MarkItemComplete(ref frameStack[depth - 1]);
                    continue;
                }
            }

            ValueParseResult valueResult = TryParseValue(ref reader, out Frame childFrame);
            if (valueResult == ValueParseResult.NeedMoreData)
            {
                return ParseStatus.NeedMoreData;
            }

            if (valueResult == ValueParseResult.InvalidData)
            {
                return ParseStatus.InvalidData;
            }

            if (valueResult == ValueParseResult.StartContainer)
            {
                if (depth == frameStack.Length)
                {
                    return ParseStatus.InvalidData;
                }

                frameStack[depth] = childFrame;
                depth++;
                continue;
            }

            MarkItemComplete(ref currentFrame);
        }

        return ParseStatus.InvalidData;
    }

    private static ValueParseResult TryParseValue(ref SequenceReader<byte> reader, out Frame childFrame)
    {
        childFrame = default;

        if (!reader.TryRead(out byte initialByte))
        {
            return ValueParseResult.NeedMoreData;
        }

        byte majorType = (byte)(initialByte >> 5);
        byte additionalInfo = (byte)(initialByte & InitialByteAdditionalInfoMask);

        if (majorType is 0 or 1)
        {
            LengthReadStatus integerStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out _);
            return ConvertLengthStatus(integerStatus);
        }

        if (majorType is MajorTypeByteString or MajorTypeTextString)
        {
            if (additionalInfo == AdditionalInfoIndefiniteLength)
            {
                childFrame = majorType == MajorTypeByteString
                    ? Frame.CreateIndefiniteByteString()
                    : Frame.CreateIndefiniteTextString();
                return ValueParseResult.StartContainer;
            }

            LengthReadStatus stringStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out ulong payloadLength);
            return stringStatus == LengthReadStatus.Success
                ? (TryAdvance(ref reader, payloadLength)
                    ? ValueParseResult.Completed
                    : ValueParseResult.NeedMoreData)
                : ConvertLengthStatus(stringStatus);
        }

        if (majorType == MajorTypeArray)
        {
            if (additionalInfo == AdditionalInfoIndefiniteLength)
            {
                childFrame = Frame.CreateIndefiniteArray();
                return ValueParseResult.StartContainer;
            }

            LengthReadStatus arrayStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out ulong itemCount);
            if (arrayStatus != LengthReadStatus.Success)
            {
                return ConvertLengthStatus(arrayStatus);
            }

            if (itemCount == 0)
            {
                return ValueParseResult.Completed;
            }

            childFrame = Frame.CreateDefinite(itemCount);
            return ValueParseResult.StartContainer;
        }

        if (majorType == MajorTypeMap)
        {
            if (additionalInfo == AdditionalInfoIndefiniteLength)
            {
                childFrame = Frame.CreateIndefiniteMap();
                return ValueParseResult.StartContainer;
            }

            LengthReadStatus mapStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out ulong pairCount);
            if (mapStatus != LengthReadStatus.Success)
            {
                return ConvertLengthStatus(mapStatus);
            }

            if (pairCount > ulong.MaxValue / 2)
            {
                return ValueParseResult.InvalidData;
            }

            ulong mapItemCount = pairCount * 2;
            if (mapItemCount == 0)
            {
                return ValueParseResult.Completed;
            }

            childFrame = Frame.CreateDefinite(mapItemCount);
            return ValueParseResult.StartContainer;
        }

        if (majorType == MajorTypeTag)
        {
            LengthReadStatus tagStatus = TryReadUnsignedArgument(ref reader, additionalInfo, out _);
            if (tagStatus != LengthReadStatus.Success)
            {
                return ConvertLengthStatus(tagStatus);
            }

            childFrame = Frame.CreateDefinite(1);
            return ValueParseResult.StartContainer;
        }

        return majorType == MajorTypeSimple
            ? additionalInfo switch
            {
                <= 23 => ValueParseResult.Completed,
                AdditionalInfoOneByte => TryReadRawBytes(ref reader, 1),
                AdditionalInfoTwoBytes => TryReadRawBytes(ref reader, 2),
                AdditionalInfoFourBytes => TryReadRawBytes(ref reader, 4),
                AdditionalInfoEightBytes => TryReadRawBytes(ref reader, 8),
                _ => ValueParseResult.InvalidData
            }
            : ValueParseResult.InvalidData;
    }

    private static bool IsDefiniteFrame(in Frame frame)
    {
        return frame.Kind is FrameKind.Root or FrameKind.DefiniteItemCount;
    }

    private static void MarkItemComplete(ref Frame frame)
    {
        if (frame.Kind is FrameKind.Root or FrameKind.DefiniteItemCount)
        {
            if (frame.RemainingItems == 0)
            {
                return;
            }

            frame.RemainingItems--;
            return;
        }

        if (frame.Kind == FrameKind.IndefiniteMap)
        {
            frame.IndefiniteMapExpectingKey = !frame.IndefiniteMapExpectingKey;
        }
    }

    private static ValueParseResult TryReadRawBytes(ref SequenceReader<byte> reader, int byteCount)
    {
        if (reader.Remaining < byteCount)
        {
            return ValueParseResult.NeedMoreData;
        }

        reader.Advance(byteCount);
        return ValueParseResult.Completed;
    }

    private static LengthReadStatus TryReadUnsignedArgument(ref SequenceReader<byte> reader, byte additionalInfo, out ulong value)
    {
        value = 0;

        if (additionalInfo < AdditionalInfoOneByte)
        {
            value = additionalInfo;
            return LengthReadStatus.Success;
        }

        if (additionalInfo == AdditionalInfoOneByte)
        {
            if (!reader.TryRead(out byte oneByteValue))
            {
                return LengthReadStatus.NeedMoreData;
            }

            value = oneByteValue;
            return LengthReadStatus.Success;
        }

        if (additionalInfo == AdditionalInfoTwoBytes)
        {
            if (!TryReadUInt16BigEndian(ref reader, out ushort twoByteValue))
            {
                return LengthReadStatus.NeedMoreData;
            }

            value = twoByteValue;
            return LengthReadStatus.Success;
        }

        if (additionalInfo == AdditionalInfoFourBytes)
        {
            if (!TryReadUInt32BigEndian(ref reader, out uint fourByteValue))
            {
                return LengthReadStatus.NeedMoreData;
            }

            value = fourByteValue;
            return LengthReadStatus.Success;
        }

        if (additionalInfo == AdditionalInfoEightBytes)
        {
            if (!TryReadUInt64BigEndian(ref reader, out ulong eightByteValue))
            {
                return LengthReadStatus.NeedMoreData;
            }

            value = eightByteValue;
            return LengthReadStatus.Success;
        }

        return LengthReadStatus.InvalidData;
    }

    private static bool TryReadUInt16BigEndian(ref SequenceReader<byte> reader, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!reader.TryCopyTo(buffer))
        {
            value = 0;
            return false;
        }

        reader.Advance(2);
        value = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        return true;
    }

    private static bool TryReadUInt32BigEndian(ref SequenceReader<byte> reader, out uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!reader.TryCopyTo(buffer))
        {
            value = 0;
            return false;
        }

        reader.Advance(4);
        value = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        return true;
    }

    private static bool TryReadUInt64BigEndian(ref SequenceReader<byte> reader, out ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (!reader.TryCopyTo(buffer))
        {
            value = 0;
            return false;
        }

        reader.Advance(8);
        value = BinaryPrimitives.ReadUInt64BigEndian(buffer);
        return true;
    }

    private static bool TryAdvance(ref SequenceReader<byte> reader, ulong byteCount)
    {
        if (byteCount > (ulong)reader.Remaining)
        {
            return false;
        }

        reader.Advance((long)byteCount);
        return true;
    }

    private static ValueParseResult ConvertLengthStatus(LengthReadStatus lengthStatus)
    {
        return lengthStatus switch
        {
            LengthReadStatus.Success => ValueParseResult.Completed,
            LengthReadStatus.NeedMoreData => ValueParseResult.NeedMoreData,
            LengthReadStatus.InvalidData => ValueParseResult.InvalidData,
            _ => ValueParseResult.InvalidData
        };
    }
}
