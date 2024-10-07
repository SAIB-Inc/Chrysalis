using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Governance;
using Chrysalis.Cardano.Models.Core.Block;

namespace Chrysalis.Cardano.Models.Core.Protocol;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(ConwayProtocolParamUpdate),
    typeof(BabbageProtocolParamUpdate),
    typeof(AlonzoProtocolParamUpdate)
])]
public interface ProtocolParamUpdate : ICbor;

[CborSerializable(CborType.Map)]
public record ConwayProtocolParamUpdate(
    [CborProperty(0)] CborUlong? MinFeeA,
    [CborProperty(1)] CborUlong? MinFeeB,
    [CborProperty(2)] CborUlong? MaxBlockBodySize,
    [CborProperty(3)] CborUlong? MaxTransactionSize,
    [CborProperty(4)] CborUlong? MaxBlockHeaderSize,
    [CborProperty(5)] CborUlong? KeyDeposit,
    [CborProperty(6)] CborUlong? PoolDeposit,
    [CborProperty(7)] CborUlong? MaximumEpoch,
    [CborProperty(8)] CborUlong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(16)] CborUlong? MinPoolCost,
    [CborProperty(17)] CborUlong? AdaPerUTxOByte,
    [CborProperty(18)] CostMdls? CostModelsForScriptLanguage,
    [CborProperty(19)] ExUnitPrices? ExecutionCosts,
    [CborProperty(20)] ExUnits? MaxTxExUnits,
    [CborProperty(21)] ExUnits? MaxBlockExUnits,
    [CborProperty(22)] CborUlong? MaxValueSize,
    [CborProperty(23)] CborUlong? CollateralPercentage,
    [CborProperty(24)] CborUlong? MaxCollateralInputs,
    [CborProperty(25)] PoolVotingThresholds? PoolVotingThresholds,
    [CborProperty(26)] DRepVotingThresholds? DRepVotingThresholds,
    [CborProperty(27)] CborUlong? MinCommitteeSize,
    [CborProperty(28)] CborUlong? CommitteeTermLimit,
    [CborProperty(29)] CborUlong? GovernanceActionValidityPeriod,
    [CborProperty(30)] CborUlong? GovernanceActionDeposit,
    [CborProperty(31)] CborUlong? DRepDeposit,
    [CborProperty(32)] CborUlong? DRepInactivityPeriod,
    [CborProperty(33)] CborRationalNumber? MinFeeRefScriptCostPerByte
) : ProtocolParamUpdate;

[CborSerializable(CborType.Map)]
public record BabbageProtocolParamUpdate(
    [CborProperty(0)] CborUlong? MinFeeA,
    [CborProperty(1)] CborUlong? MinFeeB,
    [CborProperty(2)] CborUlong? MaxBlockBodySize,
    [CborProperty(3)] CborUlong? MaxTransactionSize,
    [CborProperty(4)] CborUlong? MaxBlockHeaderSize,
    [CborProperty(5)] CborUlong? KeyDeposit,
    [CborProperty(6)] CborUlong? PoolDeposit,
    [CborProperty(7)] CborUlong? MaximumEpoch,
    [CborProperty(8)] CborUlong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(14)] ProtocolVersion? ProtocolVersion,
    [CborProperty(16)] CborUlong? MinPoolCost,
    [CborProperty(17)] CborUlong? AdaPerUTxOByte,
    [CborProperty(18)] CostMdls? CostModelsForScriptLanguage,
    [CborProperty(19)] ExUnitPrices? ExecutionCosts,
    [CborProperty(20)] ExUnits? MaxTxExUnits,
    [CborProperty(21)] ExUnits? MaxBlockExUnits,
    [CborProperty(22)] CborUlong? MaxValueSize,
    [CborProperty(23)] CborUlong? CollateralPercentage,
    [CborProperty(24)] CborUlong? MaxCollateralInputs
) : ProtocolParamUpdate;

[CborSerializable(CborType.Map)]
public record AlonzoProtocolParamUpdate(
    [CborProperty(0)] CborUlong? MinFeeA,
    [CborProperty(1)] CborUlong? MinFeeB,
    [CborProperty(2)] CborUlong? MaxBlockBodySize,
    [CborProperty(3)] CborUlong? MaxTransactionSize,
    [CborProperty(4)] CborUlong? MaxBlockHeaderSize,
    [CborProperty(5)] CborUlong? KeyDeposit,
    [CborProperty(6)] CborUlong? PoolDeposit,
    [CborProperty(7)] CborUlong? MaximumEpoch,
    [CborProperty(8)] CborUlong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(12)] CborRationalNumber? DecentralizationConstant,
    [CborProperty(13)] Nonce? ExtraEntropy,
    [CborProperty(14)] ProtocolVersion? ProtocolVersion,
    [CborProperty(16)] CborUlong? MinPoolCost,
    [CborProperty(17)] CborUlong? AdaPerUTxOByte,
    [CborProperty(18)] CostMdls? CostModelsForScriptLanguage,
    [CborProperty(19)] ExUnitPrices? ExecutionCosts,
    [CborProperty(20)] ExUnits? MaxTxExUnits,
    [CborProperty(21)] ExUnits? MaxBlockExUnits,
    [CborProperty(22)] CborUlong? MaxValueSize,
    [CborProperty(23)] CborUlong? CollateralPercentage,
    [CborProperty(24)] CborUlong? MaxCollateralInputs
) : ProtocolParamUpdate;



public record ProposedProtocolParameterUpdates(Dictionary<CborBytes, ProtocolParamUpdate> Value)
    : CborMap<CborBytes, ProtocolParamUpdate>(Value), ICbor;
