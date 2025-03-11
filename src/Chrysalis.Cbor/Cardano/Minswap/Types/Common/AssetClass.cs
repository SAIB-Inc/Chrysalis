using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Minswap.Types.Common;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record AssetClass(
    [CborIndex(0)]
    CborBytes PolicyId,

    [CborIndex(1)]
    CborBytes AssetName
) : CborBase;