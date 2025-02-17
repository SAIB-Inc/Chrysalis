

using Chrysalis.Cardano.Minswap.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Functional;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Minswap.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record MinswapLiquidityPool(
    [CborProperty(0)]
    Inline<Credential> StakeCredential,
    
    [CborProperty(1)]
    AssetClass AssetX,
    
    [CborProperty(2)]
    AssetClass AssetY,
    
    [CborProperty(3)]
    CborUlong total_liquidity,
    
    [CborProperty(4)]
    CborUlong reserve_a,
    
    [CborProperty(5)]
    CborUlong reserve_b,
    
    [CborProperty(6)]
    CborUlong base_fee_a_numerator,

    [CborProperty(7)]
    CborUlong base_fee_b_numerator,
    
    [CborProperty(8)]
    Option<CborUlong> fee_sharing_numerator_opt,

    [CborProperty(9)]
    Bool allow_dynamic_fee

) : CborBase;












