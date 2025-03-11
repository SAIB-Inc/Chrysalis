using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Plutus.Types;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record OutputReference(
    [CborIndex(0)]
    TransactionId TransactionId,

    [CborIndex(1)]
    CborUlong Index
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record TransactionId(CborBytes Hash) : CborBase;