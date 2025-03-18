using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

// [CborSerializable]
[CborList]
public partial record Anchor(
    [CborIndex(0)] string AnchorUrl,
    [CborIndex(1)] byte[] AnchorDataHash
) : CborBase<Anchor>;