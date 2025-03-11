using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Sundae.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record SundaeSwapLiquidityPool(
    [CborIndex(0)]
    CborBytes Identifier,

    [CborIndex(1)]
    AssetClassTuple Assets,

    [CborIndex(2)]
    CborUlong CirculatingLp,

    [CborIndex(3)]
    CborUlong BidFeesPer10Thousand,

    [CborIndex(4)]
    CborUlong AskFeesPer10Thousand,

    [CborIndex(5)]
    Option<MultisigScript> FeeManager,

    [CborIndex(6)]
    CborUlong MarketOpen,

    [CborIndex(7)]
    CborUlong ProtocolFees
) : CborBase;
