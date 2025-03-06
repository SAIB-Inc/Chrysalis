using System;
using System.Buffers;
using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Represents the header of a multiplexer segment containing routing and protocol information.
/// </summary>
/// <param name="TransmissionTime">The transmission timestamp in milliseconds since session start.</param>
/// <param name="ProtocolId">The protocol type identifier for routing.</param>
/// <param name="PayloadLength">The length of the payload in bytes.</param>
/// <param name="Mode">The mode flag (true for responder, false for initiator).</param>
/// <remarks>
/// This is a lightweight value type optimized for efficient processing.
/// When passing to methods, use the 'in' modifier to avoid copying.
/// </remarks>
public readonly record struct MuxSegmentHeader(
    uint TransmissionTime,
    ProtocolType ProtocolId,
    ushort PayloadLength,
    bool Mode
);

/// <summary>
/// Represents a complete multiplexer segment with header and payload data.
/// </summary>
/// <param name="Header">The segment header containing metadata and routing information.</param>
/// <param name="Payload">The binary payload data.</param>
/// <remarks>
/// MuxSegment is the fundamental unit of data transfer in the multiplexing protocol.
/// It encapsulates both the protocol metadata (Header) and the actual message data (Payload).
/// As a value type, it should be passed using the 'in' modifier to avoid copying.
/// </remarks>
public readonly record struct MuxSegment(
    MuxSegmentHeader Header,
    ReadOnlySequence<byte> Payload
);