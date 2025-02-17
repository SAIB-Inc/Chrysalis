using Chrysalis.Cardano.Splash.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Splash.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record SplashLiquidityPool(
    [CborProperty(0)]
    AssetClass PoolNft,
    
    [CborProperty(1)]
    AssetClass AssetX,
    
    [CborProperty(2)]
    AssetClass AssetY,
    
    [CborProperty(3)]
    AssetClass AssetLq,
    
    [CborProperty(4)]
    CborUlong Fee1,
    
    [CborProperty(5)]
    CborUlong Fee2,
    
    [CborProperty(6)]
    CborUlong Fee3,

    [CborProperty(7)]
    CborUlong Fee4,
    
    [CborProperty(8)]
    CborMaybeIndefList<Inline<Credential>> Verification,

    [CborProperty(9)]
    CborUlong MarketOpen,

    [CborProperty(10)]
    CborBytes Last

) : CborBase;