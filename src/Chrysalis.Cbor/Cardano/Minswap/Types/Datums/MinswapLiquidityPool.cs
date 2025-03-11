
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Minswap.Types.Common;
using Chrysalis.Cbor.Plutus.Types.Address;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using AssetClass = Chrysalis.Cbor.Cardano.Minswap.Types.Common.AssetClass;

namespace Chrysalis.Cbor.Cardano.Minswap.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record MinswapLiquidityPool(
    [CborIndex(0)]
    Inline<Credential> StakeCredential,

    [CborIndex(1)]
    AssetClass AssetX,

    [CborIndex(2)]
    AssetClass AssetY,

    [CborIndex(3)]
    CborUlong TotalLiquidity,

    [CborIndex(4)]
    CborUlong ReserveA,

    [CborIndex(5)]
    CborUlong ReserveB,

    [CborIndex(6)]
    CborUlong BaseFeeAnumerator,

    [CborIndex(7)]
    CborUlong BaseFeeBNumerator,

    [CborIndex(8)]
    Option<CborUlong> FeeSharingNumeratorOpt,

    [CborIndex(9)]
    Bool AllowDynamicFee

) : CborBase;












