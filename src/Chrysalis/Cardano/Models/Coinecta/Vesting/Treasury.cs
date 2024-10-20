using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Sundae;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cardano.Models.Mpf;

namespace Chrysalis.Cardano.Models.Coinecta.Vesting;

[CborSerializable(CborType.Constr, Index = 0)]
public record Treasury(
    MultisigScript Owner,
    CborBytes TreasuryRootHash,
    PosixTime UnlockTime
) : RawCbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(TreasuryClaimRedeemer), typeof(TreasuryWithdrawRedeemer)])]
public record TreasuryRedeemer : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record TreasuryClaimRedeemer(
    [CborProperty(0)]
    Proof Proof,

    [CborProperty(1)]
    ClaimEntry ClaimEntry
) : TreasuryRedeemer;

[CborSerializable(CborType.Constr, Index = 1)]
public record TreasuryWithdrawRedeemer() : TreasuryRedeemer;

[CborSerializable(CborType.Constr, Index = 0)]
public record ClaimEntry(
    [CborProperty(0)]
    MultisigScript Claimant,

    [CborProperty(1)]
    MultiAssetOutput VestingValue,

    [CborProperty(2)]
    MultiAssetOutput DirectValue,

    [CborProperty(3)]
    CborBytes VestingParameters,

    [CborProperty(4)]
    CborBytes VestingProgram
) : RawCbor;