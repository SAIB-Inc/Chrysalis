using Chrysalis.Cardano.Core.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body;

[CborConverter(typeof(UnionConverter))]
public abstract record TransactionBody : CborBase;


[CborConverter(typeof(CustomMapConverter))]
public record ConwayTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] CborUlong Fee,
    [CborProperty(3)] CborUlong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(7)] CborBytes? AuxiliaryDataHash,
    [CborProperty(8)] CborUlong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] CborBytes? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborProperty(15)] CborInt? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] CborUlong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs,
    [CborProperty(19)] VotingProcedures VotingProcedures,
    [CborProperty(20)] CborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
    [CborProperty(21)] CborUlong? TreasuryValue,
    [CborProperty(22)] CborUlong? Donation
) : TransactionBody;

[CborConverter(typeof(CustomMapConverter))]
public record BabbageTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] CborUlong Fee,
    [CborProperty(3)] CborUlong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(6)] Update? Update,
    [CborProperty(7)] CborBytes? AuxiliaryDataHash,
    [CborProperty(8)] CborUlong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] CborBytes? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborProperty(15)] CborInt? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] CborUlong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs
) : TransactionBody;

[CborConverter(typeof(CustomMapConverter))]
public record AlonzoTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] CborUlong Fee,
    [CborProperty(3)] CborUlong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(6)] Update? Update,
    [CborProperty(7)] CborBytes? AuxiliaryDataHash,
    [CborProperty(8)] CborUlong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] CborBytes? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<CborBytes>? RequiredSigners,
    [CborProperty(15)] CborInt? NetworkId
) : TransactionBody;