using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Sundae.Types.Common;

[CborConverter(typeof(UnionConverter))]
public abstract partial record AssetClass : CborBase;

[CborConverter(typeof(ListConverter))]
public partial record AssetClassIndefinite(List<CborBytes> Value) : AssetClass;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record AssetClassDefinite(List<CborBytes> Value) : AssetClass;

[CborConverter(typeof(UnionConverter))]
public abstract partial record AssetClassTuple : CborBase;

[CborConverter(typeof(ListConverter))]
public partial record AssetClassTupleIndef(List<AssetClass> Value) : AssetClassTuple;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record AssetClassTupleDef(List<AssetClass> Value) : AssetClassTuple;