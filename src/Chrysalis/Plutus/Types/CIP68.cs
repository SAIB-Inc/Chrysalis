
using Chrysalis.Cardano.Core.Types.Block.Transaction;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Plutus.Types;



[CborConverter(typeof(UnionConverter))]
public abstract record Cip68<T> : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Cip68WithoutExtra<T>(
    [CborProperty(0)]
    CborMap<CborBytes, TransactionMetadatum> Metadata,

    [CborProperty(1)]
    CborInt Version
) : Cip68<T>;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Cip68WithExtra<T>(
    [CborProperty(0)]
    CborMap<CborBytes, TransactionMetadatum> Metadata,

    [CborProperty(1)]
    CborInt Version,

    [CborProperty(2)]
    T PlutusData
) : Cip68<T> where T : CborBase;

