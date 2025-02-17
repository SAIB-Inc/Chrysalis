using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborConverter(typeof(UnionConverter))]
public abstract record TransactionBody : CborBase;

[CborConverter(typeof(CustomMapConverter))]
public record AlonzoTransactionBody(
    [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborIndex(1)] CborMaybeIndefList<AlonzoTransactionOutput> Outputs,
    [CborIndex(2)] CborUlong Fee,
    [CborIndex(3)] CborUlong? TimeToLive,
    [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborIndex(5)] Withdrawals? Withdrawals,
    [CborIndex(6)] Update? Update,
    [CborIndex(7)] CborBytes? AuxiliaryDataHash,
    [CborIndex(8)] CborUlong? ValidityIntervalStart,
    [CborIndex(9)] MultiAssetMint? Mint,
    [CborIndex(11)] CborBytes? ScriptDataHash,
    [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborIndex(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborIndex(15)] CborInt? NetworkId
) : TransactionBody;

[CborConverter(typeof(CustomMapConverter))]
public record BabbageTransactionBody(
    [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborIndex(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborIndex(2)] CborUlong Fee,
    [CborIndex(3)] CborUlong? TimeToLive,
    [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborIndex(5)] Withdrawals? Withdrawals,
    [CborIndex(6)] Update? Update,
    [CborIndex(7)] CborBytes? AuxiliaryDataHash,
    [CborIndex(8)] CborUlong? ValidityIntervalStart,
    [CborIndex(9)] MultiAssetMint? Mint,
    [CborIndex(11)] CborBytes? ScriptDataHash,
    [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborIndex(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborIndex(15)] CborInt? NetworkId,
    [CborIndex(16)] TransactionOutput? CollateralReturn,
    [CborIndex(17)] CborUlong? TotalCollateral,
    [CborIndex(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs
) : TransactionBody;


[CborConverter(typeof(CustomMapConverter))]
public record ConwayTransactionBody(
    [CborIndex(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborIndex(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborIndex(2)] CborUlong Fee,
    [CborIndex(3)] CborUlong? TimeToLive,
    [CborIndex(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborIndex(5)] Withdrawals? Withdrawals,
    [CborIndex(7)] CborBytes? AuxiliaryDataHash,
    [CborIndex(8)] CborUlong? ValidityIntervalStart,
    [CborIndex(9)] MultiAssetMint? Mint,
    [CborIndex(11)] CborBytes? ScriptDataHash,
    [CborIndex(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborIndex(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborIndex(15)] CborInt? NetworkId,
    [CborIndex(16)] TransactionOutput? CollateralReturn,
    [CborIndex(17)] CborUlong? TotalCollateral,
    [CborIndex(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs,
    [CborIndex(19)] VotingProcedures VotingProcedures,
    [CborIndex(20)] CborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
    [CborIndex(21)] CborUlong? TreasuryValue,
    [CborIndex(22)] CborUlong? Donation
) : TransactionBody;