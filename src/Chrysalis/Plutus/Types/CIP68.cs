
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
    Cip68Metadata Metadata,

    [CborProperty(1)]
    CborInt Version
) : Cip68<T>;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Cip68WithExtra<T>(
    [CborProperty(0)]
    Cip68Metadata Metadata,

    [CborProperty(1)]
    CborInt Version,

    [CborProperty(2)]
    T PlutusData
) : Cip68<T> where T : CborBase;

[CborConverter(typeof(UnionConverter))]
public abstract record Cip68Metadata : CborBase;


[CborConverter(typeof(UnionConverter))]
public abstract record Cip68BigInt : Cip68Metadata;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
[CborTag(2)]
public record Cip68BigUint(byte[] Value) : Cip68BigInt;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
[CborTag(3)]
public record Cip68BigNint(byte[] Value) : Cip68BigInt;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
public record Cip68BoundedBytes(byte[] Value) : Cip68Metadata;


[CborConverter(typeof(MapConverter))]
public record Cip68Map(Dictionary<Cip68Metadata, Cip68Metadata> Value) : Cip68Metadata;


[CborConverter(typeof(ListConverter))]
public record Cip68List(List<Cip68Metadata> Value) : Cip68Metadata;



