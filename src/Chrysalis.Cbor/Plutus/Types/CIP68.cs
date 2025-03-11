using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Plutus.Types;

[CborConverter(typeof(UnionConverter))]
public abstract partial record Cip68<T> : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Cip68WithoutExtra<T>(
    [CborIndex(0)]
    PlutusData Metadata,

    [CborIndex(1)]
    CborInt Version
) : Cip68<T>;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Cip68WithExtra<T>(
    [CborIndex(0)]
    PlutusData Metadata,

    [CborIndex(1)]
    CborInt Version,

    [CborIndex(2)]
    T Extra
) : Cip68<T> where T : CborBase;



