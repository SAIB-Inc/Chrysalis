using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Minswap.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record AssetClass(
    [CborProperty(0)]
    CborBytes PolicyId,

    [CborProperty(1)]
    CborBytes AssetName
) : CborBase;