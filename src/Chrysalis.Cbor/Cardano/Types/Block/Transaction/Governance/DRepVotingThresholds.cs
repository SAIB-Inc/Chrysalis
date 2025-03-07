using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record DRepVotingThresholds(
    [CborIndex(0)] CborRationalNumber MotionNoConfidence,
    [CborIndex(1)] CborRationalNumber CommitteeNormal,
    [CborIndex(2)] CborRationalNumber CommitteeNoConfidence,
    [CborIndex(3)] CborRationalNumber UpdateConstitution,
    [CborIndex(4)] CborRationalNumber HardForkInitiation,
    [CborIndex(5)] CborRationalNumber PpNetworkGroup,
    [CborIndex(6)] CborRationalNumber PpEconomicGroup,
    [CborIndex(7)] CborRationalNumber PpTechnicalGroup,
    [CborIndex(8)] CborRationalNumber PpGovernanceGroup,
    [CborIndex(9)] CborRationalNumber TreasuryWithdrawal
) : CborBase;