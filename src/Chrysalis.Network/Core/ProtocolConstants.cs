namespace Chrysalis.Network.Core;

/// <summary>
/// Constants for the Ouroboros multiplexer segment protocol.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>Maximum payload length in bytes for a single multiplexer segment (65535).</summary>
    public const int MaxSegmentPayloadLength = ushort.MaxValue;

    /// <summary>Size in bytes of the multiplexer segment header (8 bytes: 4 timestamp + 2 protocol ID + 2 payload length).</summary>
    public const ushort SegmentHeaderSize = 8;
}