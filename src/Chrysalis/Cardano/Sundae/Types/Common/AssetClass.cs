using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Sundae.Types.Common;


[CborConverter(typeof(UnionConverter))]
public abstract record AssetClass : CborBase;

[CborConverter(typeof(ListConverter))]
public record AssetClassIndefinite(List<CborBytes> Value) : AssetClass;

[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record AssetClassDefinite(List<CborBytes> Value) : AssetClass;



[CborConverter(typeof(UnionConverter))]
public abstract record AssetClassTuple : CborBase;

[CborConverter(typeof(ListConverter))]
public record AssetClassTupleIndef(List<AssetClass> Value) : AssetClassTuple;

[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record AssetClassTupleDef(List<AssetClass> Value) : AssetClassTuple;