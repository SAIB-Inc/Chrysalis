using System;
using System.Buffers;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

public readonly record struct MuxSegmentHeader(
    uint TransmissionTime,
    ProtocolType ProtocolId,
    ushort PayloadLength,
    bool Mode
);

/// <summary>
/// Represents a multiplexer segment which contains header information and a payload.
/// </summary>
public readonly record struct MuxSegment(
    MuxSegmentHeader Header,
    ReadOnlySequence<byte> Payload
);