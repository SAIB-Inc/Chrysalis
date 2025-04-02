using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborSerializable]
public partial record CurrentEraQueryResponse(
    ulong CurrentEra
) : CborBase;

[CborSerializable]
[CborList]
public partial record UtxoByAddressResponse(
    [CborOrder(0)] Dictionary<TransactionInput, TransactionOutput> Utxos
) : CborBase;

[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] byte[] TxHash,
    [CborOrder(1)] ulong Index
) : CborBase;

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

[CborSerializable]
[CborList]
public partial record CurrentProtocolParamsResponse(
    [CborOrder(0)] ProtocolParams ProtocolParams
) : CborBase;

[CborSerializable]
public partial record CurrentEra(ulong Era) : CborBase;
