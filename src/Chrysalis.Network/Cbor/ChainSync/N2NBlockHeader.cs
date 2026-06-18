using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

/// <summary>
/// The node-to-node (N2N) <c>MsgRollForward</c> payload for Shelley-era-and-later blocks: a CBOR
/// array <c>[era, #6.24(header)]</c> — the era tag followed by the tag-24-wrapped block header.
/// </summary>
/// <param name="EraTag">The era identifier (1 = Shelley, … 6 = Conway).</param>
/// <param name="HeaderCbor">The tag-24-wrapped block header bytes.</param>
[CborSerializable]
[CborList]
public partial record N2NBlockHeader(
    [CborOrder(0)] int EraTag,
    [CborOrder(1)] CborEncodedValue HeaderCbor
) : CborRecord;

/// <summary>
/// Node-to-node (N2N) <c>MsgRollForward</c> — chain-sync index 2, carrying an
/// <c>[era, #6.24(header)]</c> header (<see cref="N2NBlockHeader"/>). It is the only chain-sync
/// next-response that differs from node-to-client; Await/RollBackward are shared. <see cref="ChainSync"/>
/// knows its protocol, so it deserializes this type directly for N2N connections (and
/// <see cref="N2CMessageRollForward"/> for N2C) — no payload-shape probe on the hot path.
/// </summary>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record N2NMessageRollForward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NBlockHeader Payload,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;
