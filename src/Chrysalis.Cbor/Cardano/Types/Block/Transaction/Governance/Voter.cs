using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborSerializable]
[CborList]
public partial record Voter(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] byte[] Hash
) : CborBase<Voter>;