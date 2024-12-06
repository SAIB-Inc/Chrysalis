using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Plutus.Types.Address;

[CborConverter(typeof(UnionConverter))]
public abstract record Referenced<T> : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Inline<T>(T Value) : Referenced<T>;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record Pointer(
    [CborProperty(0)]
    CborUlong SlotNumber,

    [CborProperty(1)]
    CborUlong TransactionIndex,

    [CborProperty(2)]
    CborUlong CertificateIndex
) : Referenced<CborBase>;