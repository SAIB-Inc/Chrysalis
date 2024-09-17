using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Governance;
using Chrysalis.Cardano.Models.Plutus;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.Map)]
public record ProtocolParamUpdate(
    [CborProperty(0)] Option<CborUlong> MinFeeA,
    [CborProperty(1)] Option<CborUlong> MinFeeB,
    [CborProperty(2)] Option<CborUlong> MaxBlockBodySize,
    [CborProperty(3)] Option<CborUlong> MaxTransactionSize,
    [CborProperty(4)] Option<CborUlong> MaxBlockHeaderSize,
    [CborProperty(5)] Option<CborUlong> KeyDeposit,
    [CborProperty(6)] Option<CborUlong> PoolDeposit,
    [CborProperty(7)] Option<CborUlong> MaximumEpoch,
    [CborProperty(8)] Option<CborUlong> DesiredNumberOfStakePools,
    [CborProperty(9)] Option<CborRationalNumber> PoolPledgeInfluence,
    [CborProperty(10)] Option<CborRationalNumber> ExpansionRate,
    [CborProperty(11)] Option<CborRationalNumber> TreasuryGrowthRate,
    [CborProperty(16)] Option<CborUlong> MinPoolCost,
    [CborProperty(17)] Option<CborUlong> AdaPerUTxOByte,
    [CborProperty(18)] Option<CostMdls> CostModelsForScriptLanguage,
    [CborProperty(19)] Option<ExUnitPrices> ExecutionCosts,
    [CborProperty(20)] Option<ExUnits> MaxTxExUnits,
    [CborProperty(21)] Option<ExUnits> MaxBlockExUnits,
    [CborProperty(22)] Option<CborUlong> MaxValueSize,
    [CborProperty(23)] Option<CborUlong> CollateralPercentage,
    [CborProperty(24)] Option<CborUlong> MaxCollateralInputs,
    [CborProperty(25)] Option<PoolVotingThresholds> PoolVotingThresholds,
    [CborProperty(26)] Option<DRepVotingThresholds> DRepVotingThresholds,
    [CborProperty(27)] Option<CborUlong> MinCommitteeSize,
    [CborProperty(28)] Option<CborUlong> CommitteeTermLimit,
    [CborProperty(29)] Option<CborUlong> GovernanceActionValidityPeriod,
    [CborProperty(30)] Option<CborUlong> GovernanceActionDeposit,
    [CborProperty(31)] Option<CborUlong> DRepDeposit,
    [CborProperty(32)] Option<CborUlong> DRepInactivityPeriod,
    [CborProperty(33)] Option<CborRationalNumber> MinFeeRefScriptCostPerByte
) : ICbor;