using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborUnion]
public partial interface IProtocolParamUpdate : ICborType;

[CborSerializable]
[CborMap]
public readonly partial record struct ConwayProtocolParamUpdate : IProtocolParamUpdate
{
    [CborProperty(0)] public partial ulong? MinFeeA { get; }
    [CborProperty(1)] public partial ulong? MinFeeB { get; }
    [CborProperty(2)] public partial ulong? MaxBlockBodySize { get; }
    [CborProperty(3)] public partial ulong? MaxTransactionSize { get; }
    [CborProperty(4)] public partial ulong? MaxBlockHeaderSize { get; }
    [CborProperty(5)] public partial ulong? KeyDeposit { get; }
    [CborProperty(6)] public partial ulong? PoolDeposit { get; }
    [CborProperty(7)] public partial ulong? MaxEpoch { get; }
    [CborProperty(8)] public partial ulong? NOpt { get; }
    [CborProperty(9)] public partial CborRationalNumber? PoolPledgeInfluence { get; }
    [CborProperty(10)] public partial CborRationalNumber? ExpansionRate { get; }
    [CborProperty(11)] public partial CborRationalNumber? TreasuryGrowthRate { get; }
    [CborProperty(16)] public partial ulong? MinPoolCost { get; }
    [CborProperty(17)] public partial ulong? AdaPerUtxoByte { get; }
    [CborProperty(18)] public partial CostMdls? CostModels { get; }
    [CborProperty(19)] public partial ExUnitPrices? ExecutionCosts { get; }
    [CborProperty(20)] public partial ExUnits? MaxTxExUnits { get; }
    [CborProperty(21)] public partial ExUnits? MaxBlockExUnits { get; }
    [CborProperty(22)] public partial ulong? MaxValueSize { get; }
    [CborProperty(23)] public partial ulong? CollateralPercentage { get; }
    [CborProperty(24)] public partial ulong? MaxCollateralInputs { get; }
    [CborProperty(25)] public partial PoolVotingThresholds? PoolVotingThresholds { get; }
    [CborProperty(26)] public partial DRepVotingThresholds? DRepVotingThresholds { get; }
    [CborProperty(27)] public partial ulong? MinCommitteeSize { get; }
    [CborProperty(28)] public partial ulong? CommitteeTermLimit { get; }
    [CborProperty(29)] public partial ulong? GovActionLifetime { get; }
    [CborProperty(30)] public partial ulong? GovActionDeposit { get; }
    [CborProperty(31)] public partial ulong? DRepDeposit { get; }
    [CborProperty(32)] public partial ulong? DRepInactivityPeriod { get; }
    [CborProperty(33)] public partial CborRationalNumber? MinFeeRefScriptCostPerByte { get; }
}

[CborSerializable]
[CborMap]
public readonly partial record struct BabbageProtocolParamUpdate : IProtocolParamUpdate
{
    [CborProperty(0)] public partial ulong? MinFeeA { get; }
    [CborProperty(1)] public partial ulong? MinFeeB { get; }
    [CborProperty(2)] public partial ulong? MaxBlockBodySize { get; }
    [CborProperty(3)] public partial ulong? MaxTransactionSize { get; }
    [CborProperty(4)] public partial ulong? MaxBlockHeaderSize { get; }
    [CborProperty(5)] public partial ulong? KeyDeposit { get; }
    [CborProperty(6)] public partial ulong? PoolDeposit { get; }
    [CborProperty(7)] public partial ulong? MaxEpoch { get; }
    [CborProperty(8)] public partial ulong? NOpt { get; }
    [CborProperty(9)] public partial CborRationalNumber? PoolPledgeInfluence { get; }
    [CborProperty(10)] public partial CborRationalNumber? ExpansionRate { get; }
    [CborProperty(11)] public partial CborRationalNumber? TreasuryGrowthRate { get; }
    [CborProperty(14)] public partial ulong? ProtocolVersionMajor { get; }
    [CborProperty(15)] public partial ulong? ProtocolVersionMinor { get; }
    [CborProperty(16)] public partial ulong? MinPoolCost { get; }
    [CborProperty(17)] public partial ulong? AdaPerUtxoByte { get; }
    [CborProperty(18)] public partial CostMdls? CostModels { get; }
    [CborProperty(19)] public partial ExUnitPrices? ExecutionCosts { get; }
    [CborProperty(20)] public partial ExUnits? MaxTxExUnits { get; }
    [CborProperty(21)] public partial ExUnits? MaxBlockExUnits { get; }
    [CborProperty(22)] public partial ulong? MaxValueSize { get; }
    [CborProperty(23)] public partial ulong? CollateralPercentage { get; }
    [CborProperty(24)] public partial ulong? MaxCollateralInputs { get; }
}

[CborSerializable]
public partial record ProposedProtocolParameterUpdates(Dictionary<ReadOnlyMemory<byte>, IProtocolParamUpdate> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
