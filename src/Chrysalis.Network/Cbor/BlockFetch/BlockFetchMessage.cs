using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.BlockFetch;

/// <summary>
/// Base union type for BlockFetch mini-protocol messages (N2N, channel 3).
/// </summary>
[CborSerializable]
[CborUnion]
public partial record BlockFetchMessage : CborBase;

/// <summary>
/// Client requests blocks in a range [from, to] inclusive.
/// Wire format: [0, Point, Point]
/// </summary>
[CborSerializable]
[CborList]
public partial record RequestRange(
    [CborOrder(0)] Value0 Idx,
    [CborOrder(1)] Point From,
    [CborOrder(2)] Point To
) : BlockFetchMessage;

/// <summary>
/// Client terminates the BlockFetch protocol.
/// Wire format: [1]
/// </summary>
[CborSerializable]
[CborList]
public partial record ClientDone(
    [CborOrder(0)] Value1 Idx
) : BlockFetchMessage;

/// <summary>
/// Server begins sending blocks for the requested range.
/// Wire format: [2]
/// </summary>
[CborSerializable]
[CborList]
public partial record StartBatch(
    [CborOrder(0)] Value2 Idx
) : BlockFetchMessage;

/// <summary>
/// Server has no blocks for the requested range.
/// Wire format: [3]
/// </summary>
[CborSerializable]
[CborList]
public partial record NoBlocks(
    [CborOrder(0)] Value3 Idx
) : BlockFetchMessage;

/// <summary>
/// Server sends a single block body wrapped in CBOR tag 24.
/// Wire format: [4, tag(24, bytes)]
/// </summary>
[CborSerializable]
[CborList]
public partial record BlockBody(
    [CborOrder(0)] Value4 Idx,
    [CborOrder(1)] CborEncodedValue Body
) : BlockFetchMessage;

/// <summary>
/// Server finished sending all blocks in the batch.
/// Wire format: [5]
/// </summary>
[CborSerializable]
[CborList]
public partial record BatchDone(
    [CborOrder(0)] Value5 Idx
) : BlockFetchMessage;

/// <summary>
/// Factory methods for constructing client-sent BlockFetch messages.
/// </summary>
public static class BlockFetchMessages
{
    public static RequestRange RequestRange(Point from, Point to)
    {
        return new(new Value0(0), from, to);
    }

    public static ClientDone ClientDone()
    {
        return new(new Value1(1));
    }
}
