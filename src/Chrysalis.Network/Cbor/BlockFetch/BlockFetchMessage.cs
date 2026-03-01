using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.BlockFetch;

[CborSerializable]
[CborUnion]
public partial record BlockFetchMessage : CborBase;

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record RequestRange(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] Point From,
    [CborOrder(2)] Point To
) : BlockFetchMessage;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record ClientDone(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record StartBatch(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

[CborSerializable]
[CborList]
[CborIndex(3)]
public partial record NoBlocks(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record BlockBody(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue Body
) : BlockFetchMessage;

[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record BatchDone(
    [CborOrder(0)] int Idx
) : BlockFetchMessage;

public static class BlockFetchMessages
{
    public static RequestRange RequestRange(Point from, Point to)
    {
        return new(0, from, to);
    }

    public static ClientDone ClientDone()
    {
        return new(1);
    }
}
