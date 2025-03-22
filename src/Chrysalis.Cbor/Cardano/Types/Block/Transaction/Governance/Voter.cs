using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record Voter(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] Hash
) : CborBase<Voter>;