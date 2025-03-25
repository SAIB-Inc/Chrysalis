using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

[CborSerializable]
[CborUnion]
public abstract partial record Referenced<T> : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Inline<T>([CborOrder(0)] T Value) : Referenced<T>;

[CborSerializable]
[CborConstr(1)]
public partial record Pointer(
    [CborOrder(0)] ulong SlotNumber,
    [CborOrder(1)] ulong TransactionIndex,
    [CborOrder(2)] ulong CertificateIndex
) : Referenced<CborBase>;