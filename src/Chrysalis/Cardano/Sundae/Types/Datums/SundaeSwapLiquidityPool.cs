using Chrysalis.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Functional;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Sundae.Types.Datums;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record SundaeSwapLiquidityPool(
    [CborProperty(0)]
    CborBytes Identifier,
    
    [CborProperty(1)]
    AssetClassTuple Assets,
    
    [CborProperty(2)]
    CborUlong CirculatingLp,
    
    [CborProperty(3)]
    CborUlong BidFeesPer10Thousand,
    
    [CborProperty(4)]
    CborUlong AskFeesPer10Thousand,
    
    [CborProperty(5)]
    Option<MultisigScript> FeeManager,
    
    [CborProperty(6)]
    CborUlong MarketOpen,
    
    [CborProperty(7)]
    CborUlong ProtocolFees
) : CborBase;
