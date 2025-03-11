using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Splash.Types.Common;
using Chrysalis.Cbor.Plutus.Types.Address;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Splash.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record SplashLiquidityPool(
    [CborIndex(0)]
    AssetClass PoolNft,

    [CborIndex(1)]
    AssetClass AssetX,

    [CborIndex(2)]
    AssetClass AssetY,

    [CborIndex(3)]
    AssetClass AssetLq,

    [CborIndex(4)]
    CborUlong Fee1,

    [CborIndex(5)]
    CborUlong Fee2,

    [CborIndex(6)]
    CborUlong Fee3,

    [CborIndex(7)]
    CborUlong Fee4,

    [CborIndex(8)]
    CborMaybeIndefList<Inline<Credential>> Verification,

    [CborIndex(9)]
    CborUlong MarketOpen,

    [CborIndex(10)]
    CborBytes Last

) : CborBase;