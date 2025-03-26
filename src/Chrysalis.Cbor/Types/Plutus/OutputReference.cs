using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus;

[CborSerializable]
[CborConstr(0)]
public partial record OutputReference(
    [CborOrder(0)] TransactionId TransactionId,
    [CborOrder(1)] ulong Index
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record TransactionId(byte[] Hash) : CborBase;