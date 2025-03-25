using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public partial record Voter(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] Hash
) : CborBase;