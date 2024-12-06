using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Transaction.Output;

[CborConverter(typeof(MapConverter))]
public record MultiAssetOutput(Dictionary<CborBytes, TokenBundleOutput> Value) : CborBase;