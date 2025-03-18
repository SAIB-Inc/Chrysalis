using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborUnion]
public abstract partial record ProtocolParamUpdate : CborBase<ProtocolParamUpdate>
{
    [CborSerializable]
    [CborList]
    public partial record ConwayProtocolParamUpdate(
        [CborIndex(0)] ulong? MinFeeA,
        [CborIndex(1)] ulong? MinFeeB,
        [CborIndex(2)] ulong? MaxBlockBodySize,
        [CborIndex(3)] ulong? MaxTransactionSize,
        [CborIndex(4)] ulong? MaxBlockHeaderSize,
        [CborIndex(5)] ulong? KeyDeposit,
        [CborIndex(6)] ulong? PoolDeposit,
        [CborIndex(7)] ulong? MaximumEpoch,
        [CborIndex(8)] ulong? DesiredNumberOfStakePools,
        [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
        [CborIndex(10)] CborRationalNumber? ExpansionRate,
        [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
        [CborIndex(16)] ulong? MinPoolCost,
        [CborIndex(17)] ulong? AdaPerUTxOByte,
        [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
        [CborIndex(19)] ExUnitPrices? ExecutionCosts,
        [CborIndex(20)] ExUnits? MaxTxExUnits,
        [CborIndex(21)] ExUnits? MaxBlockExUnits,
        [CborIndex(22)] ulong? MaxValueSize,
        [CborIndex(23)] ulong? CollateralPercentage,
        [CborIndex(24)] ulong? MaxCollateralInputs,
        [CborIndex(25)] PoolVotingThresholds? PoolVotingThresholds,
        [CborIndex(26)] DRepVotingThresholds? DRepVotingThresholds,
        [CborIndex(27)] ulong? MinCommitteeSize,
        [CborIndex(28)] ulong? CommitteeTermLimit,
        [CborIndex(29)] ulong? GovernanceActionValidityPeriod,
        [CborIndex(30)] ulong? GovernanceActionDeposit,
        [CborIndex(31)] ulong? DRepDeposit,
        [CborIndex(32)] ulong? DRepInactivityPeriod,
        [CborIndex(33)] CborRationalNumber? MinFeeRefScriptCostPerByte
    ) : ProtocolParamUpdate;

    [CborSerializable]
    [CborMap]
    public partial record BabbageProtocolParamUpdate(
        [CborIndex(0)] ulong? MinFeeA,
        [CborIndex(1)] ulong? MinFeeB,
        [CborIndex(2)] ulong? MaxBlockBodySize,
        [CborIndex(3)] ulong? MaxTransactionSize,
        [CborIndex(4)] ulong? MaxBlockHeaderSize,
        [CborIndex(5)] ulong? KeyDeposit,
        [CborIndex(6)] ulong? PoolDeposit,
        [CborIndex(7)] ulong? MaximumEpoch,
        [CborIndex(8)] ulong? DesiredNumberOfStakePools,
        [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
        [CborIndex(10)] CborRationalNumber? ExpansionRate,
        [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
        [CborIndex(14)] ProtocolVersion? ProtocolVersion,
        [CborIndex(16)] ulong? MinPoolCost,
        [CborIndex(17)] ulong? AdaPerUTxOByte,
        [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
        [CborIndex(19)] ExUnitPrices? ExecutionCosts,
        [CborIndex(20)] ExUnits? MaxTxExUnits,
        [CborIndex(21)] ExUnits? MaxBlockExUnits,
        [CborIndex(22)] ulong? MaxValueSize,
        [CborIndex(23)] ulong? CollateralPercentage,
        [CborIndex(24)] ulong? MaxCollateralInputs
    ) : ProtocolParamUpdate;

    [CborSerializable]
    [CborMap]
    public partial record AlonzoProtocolParamUpdate(
        [CborIndex(0)] ulong? MinFeeA,
        [CborIndex(1)] ulong? MinFeeB,
        [CborIndex(2)] ulong? MaxBlockBodySize,
        [CborIndex(3)] ulong? MaxTransactionSize,
        [CborIndex(4)] ulong? MaxBlockHeaderSize,
        [CborIndex(5)] ulong? KeyDeposit,
        [CborIndex(6)] ulong? PoolDeposit,
        [CborIndex(7)] ulong? MaximumEpoch,
        [CborIndex(8)] ulong? DesiredNumberOfStakePools,
        [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
        [CborIndex(10)] CborRationalNumber? ExpansionRate,
        [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
        [CborIndex(12)] CborRationalNumber? DecentralizationConstant,
        [CborIndex(13)] Nonce? ExtraEntropy,
        [CborIndex(14)] ProtocolVersion? ProtocolVersion,
        [CborIndex(16)] ulong? MinPoolCost,
        [CborIndex(17)] ulong? AdaPerUTxOByte,
        [CborIndex(18)] CostMdls? CostModelsForScriptLanguage,
        [CborIndex(19)] ExUnitPrices? ExecutionCosts,
        [CborIndex(20)] ExUnits? MaxTxExUnits,
        [CborIndex(21)] ExUnits? MaxBlockExUnits,
        [CborIndex(22)] ulong? MaxValueSize,
        [CborIndex(23)] ulong? CollateralPercentage,
        [CborIndex(24)] ulong? MaxCollateralInputs
    ) : ProtocolParamUpdate;

    [CborSerializable]
    [CborMap]
    public partial record MaryProtocolParamUpdate(
        [CborIndex(0)] ulong? MinFeeA,
        [CborIndex(1)] ulong? MinFeeB,
        [CborIndex(2)] ulong? MaxBlockBodySize,
        [CborIndex(3)] ulong? MaxTransactionSize,
        [CborIndex(4)] ulong? MaxBlockHeaderSize,
        [CborIndex(5)] ulong? KeyDeposit,
        [CborIndex(6)] ulong? PoolDeposit,
        [CborIndex(7)] ulong? MaximumEpoch,
        [CborIndex(8)] ulong? DesiredNumberOfStakePools,
        [CborIndex(9)] CborRationalNumber? PoolPledgeInfluence,
        [CborIndex(10)] CborRationalNumber? ExpansionRate,
        [CborIndex(11)] CborRationalNumber? TreasuryGrowthRate,
        [CborIndex(12)] CborRationalNumber? DecentralizationConstant,
        [CborIndex(13)] Nonce? ExtraEntropy,
        [CborIndex(14)] ProtocolVersion? ProtocolVersion,
        [CborIndex(15)] ulong? Coin
    ) : ProtocolParamUpdate;
}


[CborSerializable]
public partial record ProposedProtocolParameterUpdates(
    Dictionary<byte[], ProtocolParamUpdate> Value
) : CborBase<ProposedProtocolParameterUpdates>;
