using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

/// <summary>
/// Abstract base for protocol parameter update proposals across different Cardano eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record ProtocolParamUpdate : CborBase
{
}

/// <summary>
/// Protocol parameter update proposal for the Conway era, including governance-specific parameters.
/// </summary>
/// <param name="MinFeeA">The linear fee coefficient (fee per byte).</param>
/// <param name="MinFeeB">The constant fee component.</param>
/// <param name="MaxBlockBodySize">The maximum block body size in bytes.</param>
/// <param name="MaxTransactionSize">The maximum transaction size in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum block header size in bytes.</param>
/// <param name="KeyDeposit">The stake key registration deposit in lovelace.</param>
/// <param name="PoolDeposit">The pool registration deposit in lovelace.</param>
/// <param name="MaximumEpoch">The maximum number of epochs for pool retirement.</param>
/// <param name="DesiredNumberOfStakePools">The target number of stake pools.</param>
/// <param name="PoolPledgeInfluence">The pool pledge influence factor.</param>
/// <param name="ExpansionRate">The monetary expansion rate.</param>
/// <param name="TreasuryGrowthRate">The treasury growth rate.</param>
/// <param name="MinPoolCost">The minimum pool cost in lovelace.</param>
/// <param name="AdaPerUTxOByte">The minimum lovelace per UTxO byte.</param>
/// <param name="CostModelsForScriptLanguage">The Plutus script cost models.</param>
/// <param name="ExecutionCosts">The execution unit prices for scripts.</param>
/// <param name="MaxTxExUnits">The maximum execution units per transaction.</param>
/// <param name="MaxBlockExUnits">The maximum execution units per block.</param>
/// <param name="MaxValueSize">The maximum serialized value size in bytes.</param>
/// <param name="CollateralPercentage">The collateral percentage required for script transactions.</param>
/// <param name="MaxCollateralInputs">The maximum number of collateral inputs.</param>
/// <param name="PoolVotingThresholds">The pool voting threshold parameters.</param>
/// <param name="DRepVotingThresholds">The DRep voting threshold parameters.</param>
/// <param name="MinCommitteeSize">The minimum constitutional committee size.</param>
/// <param name="CommitteeTermLimit">The maximum committee member term in epochs.</param>
/// <param name="GovernanceActionValidityPeriod">The governance action validity period in epochs.</param>
/// <param name="GovernanceActionDeposit">The governance action deposit in lovelace.</param>
/// <param name="DRepDeposit">The DRep registration deposit in lovelace.</param>
/// <param name="DRepInactivityPeriod">The DRep inactivity period in epochs.</param>
/// <param name="MinFeeRefScriptCostPerByte">The minimum fee per reference script byte.</param>
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

/// <summary>
/// Protocol parameter update proposal for the Babbage era.
/// </summary>
/// <param name="MinFeeA">The linear fee coefficient (fee per byte).</param>
/// <param name="MinFeeB">The constant fee component.</param>
/// <param name="MaxBlockBodySize">The maximum block body size in bytes.</param>
/// <param name="MaxTransactionSize">The maximum transaction size in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum block header size in bytes.</param>
/// <param name="KeyDeposit">The stake key registration deposit in lovelace.</param>
/// <param name="PoolDeposit">The pool registration deposit in lovelace.</param>
/// <param name="MaximumEpoch">The maximum number of epochs for pool retirement.</param>
/// <param name="DesiredNumberOfStakePools">The target number of stake pools.</param>
/// <param name="PoolPledgeInfluence">The pool pledge influence factor.</param>
/// <param name="ExpansionRate">The monetary expansion rate.</param>
/// <param name="TreasuryGrowthRate">The treasury growth rate.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="MinPoolCost">The minimum pool cost in lovelace.</param>
/// <param name="AdaPerUTxOByte">The minimum lovelace per UTxO byte.</param>
/// <param name="CostModelsForScriptLanguage">The Plutus script cost models.</param>
/// <param name="ExecutionCosts">The execution unit prices for scripts.</param>
/// <param name="MaxTxExUnits">The maximum execution units per transaction.</param>
/// <param name="MaxBlockExUnits">The maximum execution units per block.</param>
/// <param name="MaxValueSize">The maximum serialized value size in bytes.</param>
/// <param name="CollateralPercentage">The collateral percentage required for script transactions.</param>
/// <param name="MaxCollateralInputs">The maximum number of collateral inputs.</param>
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

/// <summary>
/// Protocol parameter update proposal for the Alonzo era, including decentralization and entropy parameters.
/// </summary>
/// <param name="MinFeeA">The linear fee coefficient (fee per byte).</param>
/// <param name="MinFeeB">The constant fee component.</param>
/// <param name="MaxBlockBodySize">The maximum block body size in bytes.</param>
/// <param name="MaxTransactionSize">The maximum transaction size in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum block header size in bytes.</param>
/// <param name="KeyDeposit">The stake key registration deposit in lovelace.</param>
/// <param name="PoolDeposit">The pool registration deposit in lovelace.</param>
/// <param name="MaximumEpoch">The maximum number of epochs for pool retirement.</param>
/// <param name="DesiredNumberOfStakePools">The target number of stake pools.</param>
/// <param name="PoolPledgeInfluence">The pool pledge influence factor.</param>
/// <param name="ExpansionRate">The monetary expansion rate.</param>
/// <param name="TreasuryGrowthRate">The treasury growth rate.</param>
/// <param name="DecentralizationConstant">The decentralization parameter.</param>
/// <param name="ExtraEntropy">The extra entropy nonce.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="MinPoolCost">The minimum pool cost in lovelace.</param>
/// <param name="AdaPerUTxOByte">The minimum lovelace per UTxO byte.</param>
/// <param name="CostModelsForScriptLanguage">The Plutus script cost models.</param>
/// <param name="ExecutionCosts">The execution unit prices for scripts.</param>
/// <param name="MaxTxExUnits">The maximum execution units per transaction.</param>
/// <param name="MaxBlockExUnits">The maximum execution units per block.</param>
/// <param name="MaxValueSize">The maximum serialized value size in bytes.</param>
/// <param name="CollateralPercentage">The collateral percentage required for script transactions.</param>
/// <param name="MaxCollateralInputs">The maximum number of collateral inputs.</param>
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

/// <summary>
/// Protocol parameter update proposal for the Mary era, including decentralization, entropy, and min UTxO coin.
/// </summary>
/// <param name="MinFeeA">The linear fee coefficient (fee per byte).</param>
/// <param name="MinFeeB">The constant fee component.</param>
/// <param name="MaxBlockBodySize">The maximum block body size in bytes.</param>
/// <param name="MaxTransactionSize">The maximum transaction size in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum block header size in bytes.</param>
/// <param name="KeyDeposit">The stake key registration deposit in lovelace.</param>
/// <param name="PoolDeposit">The pool registration deposit in lovelace.</param>
/// <param name="MaximumEpoch">The maximum number of epochs for pool retirement.</param>
/// <param name="DesiredNumberOfStakePools">The target number of stake pools.</param>
/// <param name="PoolPledgeInfluence">The pool pledge influence factor.</param>
/// <param name="ExpansionRate">The monetary expansion rate.</param>
/// <param name="TreasuryGrowthRate">The treasury growth rate.</param>
/// <param name="DecentralizationConstant">The decentralization parameter.</param>
/// <param name="ExtraEntropy">The extra entropy nonce.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="Coin">The minimum UTxO coin value in lovelace.</param>
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

/// <summary>
/// Maps genesis delegate key hashes to their proposed protocol parameter updates.
/// </summary>
/// <param name="Value">Dictionary mapping genesis delegate key hashes to parameter update proposals.</param>
[CborSerializable]
public partial record ProposedProtocolParameterUpdates(
    Dictionary<ReadOnlyMemory<byte>, ProtocolParamUpdate> Value
) : CborBase;
