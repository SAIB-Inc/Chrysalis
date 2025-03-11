using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Coinecta.Types.Common;
using Chrysalis.Cbor.Cardano.MPF.Types.Common;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Coinecta.Types.Redeemers;

[CborConverter(typeof(UnionConverter))]
public partial record TreasuryRedeemer : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record TreasuryClaimRedeemer(
    [CborIndex(0)]
    Proof Proof,

    [CborIndex(1)]
    ClaimEntry ClaimEntry
) : TreasuryRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record TreasuryWithdrawRedeemer() : TreasuryRedeemer;
