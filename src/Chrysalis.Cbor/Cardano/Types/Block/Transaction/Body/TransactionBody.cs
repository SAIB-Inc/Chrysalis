using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TransactionOutput;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborSerializable]
[CborUnion]
public abstract partial record TransactionBody : CborBase<TransactionBody>
{
}

[CborSerializable]
[CborMap]
public partial record AlonzoTransactionBody(
        [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
        [CborProperty(1)] CborMaybeIndefList<AlonzoTransactionOutput> Outputs,
        [CborProperty(2)] ulong Fee,
        [CborProperty(3)] ulong? TimeToLive,
        [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
        [CborProperty(5)] Withdrawals? Withdrawals,
        [CborProperty(6)] Update? Update,
        [CborProperty(7)] byte[]? AuxiliaryDataHash,
        [CborProperty(8)] ulong? ValidityIntervalStart,
        [CborProperty(9)] MultiAssetMint? Mint,
        [CborProperty(11)] byte[]? ScriptDataHash,
        [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
        [CborProperty(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
        [CborProperty(15)] int? NetworkId
    ) : TransactionBody;

[CborSerializable]
[CborMap]
public partial record BabbageTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] ulong Fee,
    [CborProperty(3)] ulong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(6)] Update? Update,
    [CborProperty(7)] byte[]? AuxiliaryDataHash,
    [CborProperty(8)] ulong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] byte[]? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
    [CborProperty(15)] byte[]? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] ulong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs
) : TransactionBody;


[CborSerializable]
[CborMap]
public partial record ConwayTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] ulong Fee,
    [CborProperty(3)] ulong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(7)] byte[]? AuxiliaryDataHash,
    [CborProperty(8)] ulong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] byte[]? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
    [CborProperty(15)] int? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] ulong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs,
    [CborProperty(19)] VotingProcedures? VotingProcedures,
    [CborProperty(20)] CborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
    [CborProperty(21)] ulong? TreasuryValue,
    [CborProperty(22)] ulong? Donation
) : TransactionBody;

