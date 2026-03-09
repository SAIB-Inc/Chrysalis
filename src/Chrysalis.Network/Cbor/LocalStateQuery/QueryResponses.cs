using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Represents the response to a current era query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="CurrentEra">The numeric identifier of the current Cardano era.</param>
[CborSerializable]
public partial record CurrentEraQueryResponse(
    ulong CurrentEra
) : CborBase;

/// <summary>
/// Represents the response to a UTxO-by-address query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Utxos">A map of transaction inputs to their corresponding transaction outputs.</param>
[CborSerializable]
[CborList]
public partial record UtxoByAddressResponse(
    [CborOrder(0)] Dictionary<TransactionInput, TransactionOutput> Utxos
) : CborBase;

/// <summary>
/// Represents the Cardano protocol parameters returned by the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="MinFeeA">The linear fee coefficient (fee per byte of transaction size).</param>
/// <param name="MinFeeB">The constant fee component added to every transaction.</param>
/// <param name="MaxBlockBodySize">The maximum size of a block body in bytes.</param>
/// <param name="MaxTransactionSize">The maximum size of a transaction in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum size of a block header in bytes.</param>
/// <param name="KeyDeposit">The deposit required to register a stake key in lovelace.</param>
/// <param name="PoolDeposit">The deposit required to register a stake pool in lovelace.</param>
/// <param name="MaximumEpoch">The maximum number of epochs a pool retirement can be scheduled in the future.</param>
/// <param name="DesiredNumberOfStakePools">The target number of stake pools (k parameter).</param>
/// <param name="PoolPledgeInfluence">The influence of pool pledge on rewards (a0 parameter).</param>
/// <param name="ExpansionRate">The monetary expansion rate (rho parameter).</param>
/// <param name="TreasuryGrowthRate">The treasury growth rate (tau parameter).</param>
/// <param name="ProtocolVersion">The current protocol version (major and minor).</param>
/// <param name="MinPoolCost">The minimum fixed cost a pool can charge per epoch in lovelace.</param>
/// <param name="AdaPerUTxOByte">The cost per byte of UTxO storage in lovelace.</param>
/// <param name="CostModelsForScriptLanguage">The cost models for Plutus script languages.</param>
/// <param name="ExecutionCosts">The prices for Plutus script execution units (memory and CPU steps).</param>
/// <param name="MaxTxExUnits">The maximum execution units allowed per transaction.</param>
/// <param name="MaxBlockExUnits">The maximum execution units allowed per block.</param>
/// <param name="MaxValueSize">The maximum size of a serialized multi-asset value in bytes.</param>
/// <param name="CollateralPercentage">The percentage of the transaction fee required as collateral.</param>
/// <param name="MaxCollateralInputs">The maximum number of collateral inputs allowed per transaction.</param>
/// <param name="PoolVotingThresholds">The voting thresholds for stake pool operator governance votes.</param>
/// <param name="DRepVotingThresholds">The voting thresholds for delegated representative governance votes.</param>
/// <param name="MinCommitteeSize">The minimum number of members required in the constitutional committee.</param>
/// <param name="CommitteeTermLimit">The maximum term length for constitutional committee members in epochs.</param>
/// <param name="GovernanceActionValidityPeriod">The number of epochs a governance action remains valid after submission.</param>
/// <param name="GovernanceActionDeposit">The deposit required to submit a governance action in lovelace.</param>
/// <param name="DRepDeposit">The deposit required to register as a delegated representative in lovelace.</param>
/// <param name="DRepInactivityPeriod">The number of epochs of inactivity after which a DRep is considered inactive.</param>
/// <param name="MinFeeRefScriptCostPerByte">The minimum fee per byte for reference scripts.</param>
[CborSerializable]
[CborList]
public partial record ProtocolParams(
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
    [CborOrder(14)] ProtocolVersion? ProtocolVersion,
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
) : CborBase;

/// <summary>
/// Represents the response to a current protocol parameters query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="ProtocolParams">The current protocol parameters of the Cardano network.</param>
[CborSerializable]
[CborList]
public partial record CurrentProtocolParamsResponse(
    [CborOrder(0)] ProtocolParams ProtocolParams
) : CborBase;

/// <summary>
/// Represents a Cardano era identifier used in LocalStateQuery responses.
/// </summary>
/// <param name="Era">The numeric identifier of the era.</param>
[CborSerializable]
public partial record CurrentEra(ulong Era) : CborBase;
