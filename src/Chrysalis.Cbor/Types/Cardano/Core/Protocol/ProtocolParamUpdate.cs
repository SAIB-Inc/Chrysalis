using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborUnion]
public abstract partial record ProtocolParamUpdate : CborBase
{
}

[CborSerializable]
[CborList]
public partial record ConwayProtocolParamUpdate(
    [CborOrder(0)] ulong? MinFeeA,
    [CborOrder(1)] ulong? MinFeeB,
    [CborOrder(2)] ulong? MaxBlockBodySize,
    [CborOrder(3)] ulong? MaxTransactionSize,
    [CborOrder(4)] ulong? MaxBlockHeaderSize,
    [CborOrder(5)] ulong? KeyDeposit,
    [CborOrder(6)] ulong? PoolDeposit,
    [CborOrder(7)] ulong? MaximumEpoch,
    [CborOrder(8)] ulong? DesiredNumberOfStakePools,
    [CborOrder(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborOrder(10)] CborRationalNumber? ExpansionRate,
    [CborOrder(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborOrder(16)] ulong? MinPoolCost,
    [CborOrder(17)] ulong? AdaPerUTxOByte,
    [CborOrder(18)] CostMdls? CostModelsForScriptLanguage,
    [CborOrder(19)] ExUnitPrices? ExecutionCosts,
    [CborOrder(20)] ExUnits? MaxTxExUnits,
    [CborOrder(21)] ExUnits? MaxBlockExUnits,
    [CborOrder(22)] ulong? MaxValueSize,
    [CborOrder(23)] ulong? CollateralPercentage,
    [CborOrder(24)] ulong? MaxCollateralInputs,
    [CborOrder(25)] PoolVotingThresholds? PoolVotingThresholds,
    [CborOrder(26)] DRepVotingThresholds? DRepVotingThresholds,
    [CborOrder(27)] ulong? MinCommitteeSize,
    [CborOrder(28)] ulong? CommitteeTermLimit,
    [CborOrder(29)] ulong? GovernanceActionValidityPeriod,
    [CborOrder(30)] ulong? GovernanceActionDeposit,
    [CborOrder(31)] ulong? DRepDeposit,
    [CborOrder(32)] ulong? DRepInactivityPeriod,
    [CborOrder(33)] CborRationalNumber? MinFeeRefScriptCostPerByte
) : ProtocolParamUpdate;

[CborSerializable]
[CborMap]
public partial record BabbageProtocolParamUpdate(
    [CborProperty(0)] ulong? MinFeeA,
    [CborProperty(1)] ulong? MinFeeB,
    [CborProperty(2)] ulong? MaxBlockBodySize,
    [CborProperty(3)] ulong? MaxTransactionSize,
    [CborProperty(4)] ulong? MaxBlockHeaderSize,
    [CborProperty(5)] ulong? KeyDeposit,
    [CborProperty(6)] ulong? PoolDeposit,
    [CborProperty(7)] ulong? MaximumEpoch,
    [CborProperty(8)] ulong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(14)] ProtocolVersion? ProtocolVersion,
    [CborProperty(16)] ulong? MinPoolCost,
    [CborProperty(17)] ulong? AdaPerUTxOByte,
    [CborProperty(18)] CostMdls? CostModelsForScriptLanguage,
    [CborProperty(19)] ExUnitPrices? ExecutionCosts,
    [CborProperty(20)] ExUnits? MaxTxExUnits,
    [CborProperty(21)] ExUnits? MaxBlockExUnits,
    [CborProperty(22)] ulong? MaxValueSize,
    [CborProperty(23)] ulong? CollateralPercentage,
    [CborProperty(24)] ulong? MaxCollateralInputs
) : ProtocolParamUpdate;

[CborSerializable]
[CborMap]
public partial record AlonzoProtocolParamUpdate(
    [CborProperty(0)] ulong? MinFeeA,
    [CborProperty(1)] ulong? MinFeeB,
    [CborProperty(2)] ulong? MaxBlockBodySize,
    [CborProperty(3)] ulong? MaxTransactionSize,
    [CborProperty(4)] ulong? MaxBlockHeaderSize,
    [CborProperty(5)] ulong? KeyDeposit,
    [CborProperty(6)] ulong? PoolDeposit,
    [CborProperty(7)] ulong? MaximumEpoch,
    [CborProperty(8)] ulong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(12)] CborRationalNumber? DecentralizationConstant,
    [CborProperty(13)] Nonce? ExtraEntropy,
    [CborProperty(14)] ProtocolVersion? ProtocolVersion,
    [CborProperty(16)] ulong? MinPoolCost,
    [CborProperty(17)] ulong? AdaPerUTxOByte,
    [CborProperty(18)] CostMdls? CostModelsForScriptLanguage,
    [CborProperty(19)] ExUnitPrices? ExecutionCosts,
    [CborProperty(20)] ExUnits? MaxTxExUnits,
    [CborProperty(21)] ExUnits? MaxBlockExUnits,
    [CborProperty(22)] ulong? MaxValueSize,
    [CborProperty(23)] ulong? CollateralPercentage,
    [CborProperty(24)] ulong? MaxCollateralInputs
) : ProtocolParamUpdate;

[CborSerializable]
[CborMap]
public partial record MaryProtocolParamUpdate(
    [CborProperty(0)] ulong? MinFeeA,
    [CborProperty(1)] ulong? MinFeeB,
    [CborProperty(2)] ulong? MaxBlockBodySize,
    [CborProperty(3)] ulong? MaxTransactionSize,
    [CborProperty(4)] ulong? MaxBlockHeaderSize,
    [CborProperty(5)] ulong? KeyDeposit,
    [CborProperty(6)] ulong? PoolDeposit,
    [CborProperty(7)] ulong? MaximumEpoch,
    [CborProperty(8)] ulong? DesiredNumberOfStakePools,
    [CborProperty(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborProperty(10)] CborRationalNumber? ExpansionRate,
    [CborProperty(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborProperty(12)] CborRationalNumber? DecentralizationConstant,
    [CborProperty(13)] Nonce? ExtraEntropy,
    [CborProperty(14)] ProtocolVersion? ProtocolVersion,
    [CborProperty(15)] ulong? Coin
) : ProtocolParamUpdate;

[CborSerializable]
public partial record ProposedProtocolParameterUpdates(
    Dictionary<byte[], ProtocolParamUpdate> Value
) : CborBase;
