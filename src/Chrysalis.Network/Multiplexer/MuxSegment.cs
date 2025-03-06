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

public readonly record struct MuxSegment(
    MuxSegmentHeader Header,
    ReadOnlySequence<byte> Payload
);