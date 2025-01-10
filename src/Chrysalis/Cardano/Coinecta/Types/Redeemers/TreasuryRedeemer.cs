using Chrysalis.Cardano.Coinecta.Types.Common;
using Chrysalis.Cardano.MPF.Types.Common;
using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cardano.Coinecta.Types.Redeemers;

[CborConverter(typeof(UnionConverter))]
public record TreasuryRedeemer : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record TreasuryClaimRedeemer(
    [CborProperty(0)]
    Proof Proof,

    [CborProperty(1)]
    ClaimEntry ClaimEntry
) : TreasuryRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record TreasuryWithdrawRedeemer() : TreasuryRedeemer;
