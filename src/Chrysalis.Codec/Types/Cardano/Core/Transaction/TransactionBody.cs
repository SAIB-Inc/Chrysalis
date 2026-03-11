using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public partial interface ITransactionBody : ICborType;

[CborSerializable]
[CborMap]
public readonly partial record struct AlonzoTransactionBody : ITransactionBody
{
    [CborProperty(0)] public partial ICborMaybeIndefList<TransactionInput> Inputs { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<ITransactionOutput> Outputs { get; }
    [CborProperty(2)] public partial ulong Fee { get; }
    [CborProperty(3)] public partial ulong? TimeToLive { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<ICertificate>? Certificates { get; }
    [CborProperty(5)] public partial Withdrawals? Withdrawals { get; }
    [CborProperty(6)] public partial Update? Update { get; }
    [CborProperty(7)] public partial ReadOnlyMemory<byte>? AuxiliaryDataHash { get; }
    [CborProperty(8)] public partial ulong? ValidityIntervalStart { get; }
    [CborProperty(9)] public partial MultiAssetMint? Mint { get; }
    [CborProperty(11)] public partial ReadOnlyMemory<byte>? ScriptDataHash { get; }
    [CborProperty(13)] public partial ICborMaybeIndefList<TransactionInput>? Collateral { get; }
    [CborProperty(14)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners { get; }
    [CborProperty(15)] public partial int? NetworkId { get; }
}

[CborSerializable]
[CborMap]
public readonly partial record struct BabbageTransactionBody : ITransactionBody
{
    [CborProperty(0)] public partial ICborMaybeIndefList<TransactionInput> Inputs { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<ITransactionOutput> Outputs { get; }
    [CborProperty(2)] public partial ulong Fee { get; }
    [CborProperty(3)] public partial ulong? TimeToLive { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<ICertificate>? Certificates { get; }
    [CborProperty(5)] public partial Withdrawals? Withdrawals { get; }
    [CborProperty(6)] public partial Update? Update { get; }
    [CborProperty(7)] public partial ReadOnlyMemory<byte>? AuxiliaryDataHash { get; }
    [CborProperty(8)] public partial ulong? ValidityIntervalStart { get; }
    [CborProperty(9)] public partial MultiAssetMint? Mint { get; }
    [CborProperty(11)] public partial ReadOnlyMemory<byte>? ScriptDataHash { get; }
    [CborProperty(13)] public partial ICborMaybeIndefList<TransactionInput>? Collateral { get; }
    [CborProperty(14)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners { get; }
    [CborProperty(15)] public partial int? NetworkId { get; }
    [CborProperty(16)] public partial ITransactionOutput? CollateralReturn { get; }
    [CborProperty(17)] public partial ulong? TotalCollateral { get; }
    [CborProperty(18)] public partial ICborMaybeIndefList<TransactionInput>? ReferenceInputs { get; }
}

[CborSerializable]
[CborMap]
public readonly partial record struct ConwayTransactionBody : ITransactionBody
{
    [CborProperty(0)] public partial ICborMaybeIndefList<TransactionInput> Inputs { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<ITransactionOutput> Outputs { get; }
    [CborProperty(2)] public partial ulong Fee { get; }
    [CborProperty(3)] public partial ulong? TimeToLive { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<ICertificate>? Certificates { get; }
    [CborProperty(5)] public partial Withdrawals? Withdrawals { get; }
    [CborProperty(7)] public partial ReadOnlyMemory<byte>? AuxiliaryDataHash { get; }
    [CborProperty(8)] public partial ulong? ValidityIntervalStart { get; }
    [CborProperty(9)] public partial MultiAssetMint? Mint { get; }
    [CborProperty(11)] public partial ReadOnlyMemory<byte>? ScriptDataHash { get; }
    [CborProperty(13)] public partial ICborMaybeIndefList<TransactionInput>? Collateral { get; }
    [CborProperty(14)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners { get; }
    [CborProperty(15)] public partial int? NetworkId { get; }
    [CborProperty(16)] public partial ITransactionOutput? CollateralReturn { get; }
    [CborProperty(17)] public partial ulong? TotalCollateral { get; }
    [CborProperty(18)] public partial ICborMaybeIndefList<TransactionInput>? ReferenceInputs { get; }
    [CborProperty(19)] public partial VotingProcedures? VotingProcedures { get; }
    [CborProperty(20)] public partial ICborMaybeIndefList<ProposalProcedure>? ProposalProcedures { get; }
    [CborProperty(21)] public partial ulong? TreasuryValue { get; }
    [CborProperty(22)] public partial ulong? Donation { get; }
}
