using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborList]
public partial record Anchor(
    [CborOrder(0)] string AnchorUrl,
    [CborOrder(1)] byte[] AnchorDataHash
) : CborBase;