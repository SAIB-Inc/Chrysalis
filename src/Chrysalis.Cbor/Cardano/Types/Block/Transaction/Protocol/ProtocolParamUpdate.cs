using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborConverter(typeof(UnionConverter))]
public abstract record ProtocolParamUpdate : CborBase;


[CborConverter(typeof(CustomMapConverter))]
public record ConwayProtocolParamUpdate(
    [CborIndex(0)] CborUlong? MinFeeA,
    [CborIndex(1)] CborUlong? MinFeeB,
    [CborIndex(2)] CborUlong? MaxBlockBodySize,
    [CborIndex(3)] CborUlong? MaxTransactionSize,
    [CborIndex(4)] CborUlong? MaxBlockHeaderSize,
    [CborIndex(5)] CborUlong? KeyDeposit,
    [CborIndex(6)] CborUlong? PoolDeposit,
    [CborIndex(7)] CborUlong? MaximumEpoch,
    [CborIndex(8)] CborUlong? DesiredNumberOfStakePools,
    [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborIndex(10)] CborRationalNumber? ExpansionRate,
    [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborIndex(16)] CborUlong? MinPoolCost,
    [CborIndex(17)] CborUlong? AdaPerUTxOByte,
    [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
    [CborIndex(19)] ExUnitPrices? ExecutionCosts,
    [CborIndex(20)] ExUnits? MaxTxExUnits,
    [CborIndex(21)] ExUnits? MaxBlockExUnits,
    [CborIndex(22)] CborUlong? MaxValueSize,
    [CborIndex(23)] CborUlong? CollateralPercentage,
    [CborIndex(24)] CborUlong? MaxCollateralInputs,
    [CborIndex(25)] PoolVotingThresholds? PoolVotingThresholds,
    [CborIndex(26)] DRepVotingThresholds? DRepVotingThresholds,
    [CborIndex(27)] CborUlong? MinCommitteeSize,
    [CborIndex(28)] CborUlong? CommitteeTermLimit,
    [CborIndex(29)] CborUlong? GovernanceActionValidityPeriod,
    [CborIndex(30)] CborUlong? GovernanceActionDeposit,
    [CborIndex(31)] CborUlong? DRepDeposit,
    [CborIndex(32)] CborUlong? DRepInactivityPeriod,
    [CborIndex(33)] CborRationalNumber? MinFeeRefScriptCostPerByte
) : ProtocolParamUpdate;

[CborConverter(typeof(CustomMapConverter))]
public record BabbageProtocolParamUpdate(
    [CborIndex(0)] CborUlong? MinFeeA,
    [CborIndex(1)] CborUlong? MinFeeB,
    [CborIndex(2)] CborUlong? MaxBlockBodySize,
    [CborIndex(3)] CborUlong? MaxTransactionSize,
    [CborIndex(4)] CborUlong? MaxBlockHeaderSize,
    [CborIndex(5)] CborUlong? KeyDeposit,
    [CborIndex(6)] CborUlong? PoolDeposit,
    [CborIndex(7)] CborUlong? MaximumEpoch,
    [CborIndex(8)] CborUlong? DesiredNumberOfStakePools,
    [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborIndex(10)] CborRationalNumber? ExpansionRate,
    [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborIndex(14)] ProtocolVersion? ProtocolVersion,
    [CborIndex(16)] CborUlong? MinPoolCost,
    [CborIndex(17)] CborUlong? AdaPerUTxOByte,
    [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
    [CborIndex(19)] ExUnitPrices? ExecutionCosts,
    [CborIndex(20)] ExUnits? MaxTxExUnits,
    [CborIndex(21)] ExUnits? MaxBlockExUnits,
    [CborIndex(22)] CborUlong? MaxValueSize,
    [CborIndex(23)] CborUlong? CollateralPercentage,
    [CborIndex(24)] CborUlong? MaxCollateralInputs
) : ProtocolParamUpdate;

[CborConverter(typeof(CustomMapConverter))]
public record AlonzoProtocolParamUpdate(
    [CborIndex(0)] CborUlong? MinFeeA,
    [CborIndex(1)] CborUlong? MinFeeB,
    [CborIndex(2)] CborUlong? MaxBlockBodySize,
    [CborIndex(3)] CborUlong? MaxTransactionSize,
    [CborIndex(4)] CborUlong? MaxBlockHeaderSize,
    [CborIndex(5)] CborUlong? KeyDeposit,
    [CborIndex(6)] CborUlong? PoolDeposit,
    [CborIndex(7)] CborUlong? MaximumEpoch,
    [CborIndex(8)] CborUlong? DesiredNumberOfStakePools,
    [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborIndex(10)] CborRationalNumber? ExpansionRate,
    [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborIndex(12)] CborRationalNumber? DecentralizationConstant,
    [CborIndex(13)] Nonce? ExtraEntropy,
    [CborIndex(14)] ProtocolVersion? ProtocolVersion,
    [CborIndex(16)] CborUlong? MinPoolCost,
    [CborIndex(17)] CborUlong? AdaPerUTxOByte,
    [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
    [CborIndex(19)] ExUnitPrices? ExecutionCosts,
    [CborIndex(20)] ExUnits? MaxTxExUnits,
    [CborIndex(21)] ExUnits? MaxBlockExUnits,
    [CborIndex(22)] CborUlong? MaxValueSize,
    [CborIndex(23)] CborUlong? CollateralPercentage,
    [CborIndex(24)] CborUlong? MaxCollateralInputs
) : ProtocolParamUpdate;

[CborConverter(typeof(CustomMapConverter))]
public record MaryProtocolParamUpdate(
    [CborIndex(0)] CborUlong? MinFeeA,
    [CborIndex(1)] CborUlong? MinFeeB,
    [CborIndex(2)] CborUlong? MaxBlockBodySize,
    [CborIndex(3)] CborUlong? MaxTransactionSize,
    [CborIndex(4)] CborUlong? MaxBlockHeaderSize,
    [CborIndex(5)] CborUlong? KeyDeposit,
    [CborIndex(6)] CborUlong? PoolDeposit,
    [CborIndex(7)] CborUlong? MaximumEpoch,
    [CborIndex(8)] CborUlong? DesiredNumberOfStakePools,
    [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
    [CborIndex(10)] CborRationalNumber? ExpansionRate,
    [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
    [CborIndex(12)] CborRationalNumber? DecentralizationConstant,
    [CborIndex(13)] Nonce? ExtraEntropy,
    [CborIndex(14)] ProtocolVersion? ProtocolVersion,
    [CborIndex(15)] CborUlong? Coin
) : ProtocolParamUpdate;


[CborConverter(typeof(MapConverter))]
public record ProposedProtocolParameterUpdates(
    Dictionary<CborBytes, ProtocolParamUpdate> Value
) : CborBase;
