using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

// [CborSerializable]
[CborUnion]
public abstract partial record TransactionBody : CborBase<TransactionBody>
{
    // [CborSerializable]
    [CborMap]
    public partial record AlonzoTransactionBody(
            [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
            [CborIndex(1)] CborMaybeIndefList<TransactionOutput.AlonzoTransactionOutput> Outputs,
            [CborIndex(2)] ulong Fee,
            [CborIndex(3)] ulong? TimeToLive,
            [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
            [CborIndex(5)] Withdrawals? Withdrawals,
            [CborIndex(6)] Update? Update,
            [CborIndex(7)] byte[]? AuxiliaryDataHash,
            [CborIndex(8)] ulong? ValidityIntervalStart,
            [CborIndex(9)] MultiAsset.MultiAssetMint? Mint,
            [CborIndex(11)] byte[]? ScriptDataHash,
            [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
            [CborIndex(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
            [CborIndex(15)] int? NetworkId
        ) : TransactionBody;

    // [CborSerializable]
    [CborMap]
    public partial record BabbageTransactionBody(
        [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
        [CborIndex(1)] CborMaybeIndefList<TransactionOutput> Outputs,
        [CborIndex(2)] ulong Fee,
        [CborIndex(3)] ulong? TimeToLive,
        [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
        [CborIndex(5)] Withdrawals? Withdrawals,
        [CborIndex(6)] Update? Update,
        [CborIndex(7)] byte[]? AuxiliaryDataHash,
        [CborIndex(8)] ulong? ValidityIntervalStart,
        [CborIndex(9)] MultiAsset.MultiAssetMint? Mint,
        [CborIndex(11)] byte[]? ScriptDataHash,
        [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
        [CborIndex(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
        [CborIndex(15)] byte[]? NetworkId,
        [CborIndex(16)] TransactionOutput? CollateralReturn,
        [CborIndex(17)] ulong? TotalCollateral,
        [CborIndex(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs
    ) : TransactionBody;


    // [CborSerializable]
    [CborMap]
    public partial record ConwayTransactionBody(
        [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
        [CborIndex(1)] CborMaybeIndefList<TransactionOutput> Outputs,
        [CborIndex(2)] ulong Fee,
        [CborIndex(3)] ulong? TimeToLive,
        [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
        [CborIndex(5)] Withdrawals? Withdrawals,
        [CborIndex(7)] byte[]? AuxiliaryDataHash,
        [CborIndex(8)] ulong? ValidityIntervalStart,
        [CborIndex(9)] MultiAsset.MultiAssetMint? Mint,
        [CborIndex(11)] byte[]? ScriptDataHash,
        [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
        [CborIndex(14)] CborMaybeIndefList<byte[]>? RequiredSigners,
        [CborIndex(15)] int? NetworkId,
        [CborIndex(16)] TransactionOutput? CollateralReturn,
        [CborIndex(17)] ulong? TotalCollateral,
        [CborIndex(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs,
        [CborIndex(19)] VotingProcedures VotingProcedures,
        [CborIndex(20)] CborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
        [CborIndex(21)] ulong? TreasuryValue,
        [CborIndex(22)] ulong? Donation
    ) : TransactionBody;
}
