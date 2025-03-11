using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public partial record DRepVotingThresholds(
    [CborIndex(0)] CborRationalNumber MotionNoConfidence,
    [CborIndex(1)] CborRationalNumber CommitteeNormal,
    [CborIndex(2)] CborRationalNumber CommitteeNoConfidence,
    [CborIndex(4)] CborRationalNumber UpdateConstitution,
    [CborIndex(5)] CborRationalNumber HardForkInitiation,
    [CborIndex(6)] CborRationalNumber PpNetworkGroup,
    [CborIndex(7)] CborRationalNumber PpEconomicGroup,
    [CborIndex(8)] CborRationalNumber PpTechnicalGroup,
    [CborIndex(9)] CborRationalNumber PpGovernanceGroup,
    [CborIndex(10)] CborRationalNumber TreasuryWithdrawal
) : CborBase;