using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract partial record TokenBundle : CborBase;


[CborConverter(typeof(MapConverter))]
public partial record TokenBundleOutput(Dictionary<CborBytes, CborUlong> Value) : TokenBundle;


[CborConverter(typeof(MapConverter))]
public partial record TokenBundleMint(Dictionary<CborBytes, CborLong> Value) : TokenBundle;