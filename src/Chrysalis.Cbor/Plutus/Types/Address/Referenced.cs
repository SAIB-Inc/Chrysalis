using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Plutus.Types.Address;

[CborConverter(typeof(UnionConverter))]
public abstract partial record Referenced<T> : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Inline<T>(T Value) : Referenced<T>;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record Pointer(
    [CborIndex(0)]
    CborUlong SlotNumber,

    [CborIndex(1)]
    CborUlong TransactionIndex,

    [CborIndex(2)]
    CborUlong CertificateIndex
) : Referenced<CborBase>;