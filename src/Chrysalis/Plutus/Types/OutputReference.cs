using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Plutus.Types;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record OutputReference(
    [CborProperty(0)]
    TransactionId TransactionId,

    [CborProperty(1)]
    CborUlong Index
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record TransactionId(CborBytes Hash) : CborBase;