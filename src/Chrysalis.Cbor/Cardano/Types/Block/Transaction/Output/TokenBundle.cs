using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract record TokenBundle : CborBase;


[CborConverter(typeof(MapConverter))]
public record TokenBundleOutput(Dictionary<CborBytes, CborUlong> Value) : TokenBundle;


[CborConverter(typeof(MapConverter))]
public record TokenBundleMint(Dictionary<CborBytes, CborLong> Value) : TokenBundle;