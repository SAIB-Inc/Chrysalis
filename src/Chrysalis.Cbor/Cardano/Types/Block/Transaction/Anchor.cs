using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborSerializable]
[CborList]
public partial record Anchor(
    [CborOrder(0)] string AnchorUrl,
    [CborOrder(1)] byte[] AnchorDataHash
) : CborBase<Anchor>;