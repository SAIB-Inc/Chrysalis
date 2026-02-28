using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// Abstract base for transaction bodies across different Cardano eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record TransactionBody : CborBase { }

/// <summary>
/// An Alonzo-era transaction body with support for Plutus scripts, collateral, and required signers.
/// </summary>
/// <param name="Inputs">The transaction inputs to spend.</param>
/// <param name="Outputs">The Alonzo-era transaction outputs.</param>
/// <param name="Fee">The transaction fee in lovelace.</param>
/// <param name="TimeToLive">The optional slot after which the transaction is invalid.</param>
/// <param name="Certificates">The optional list of certificates.</param>
/// <param name="Withdrawals">The optional stake reward withdrawals.</param>
/// <param name="Update">The optional protocol parameter update proposal.</param>
/// <param name="AuxiliaryDataHash">The optional hash of the auxiliary data.</param>
/// <param name="ValidityIntervalStart">The optional slot before which the transaction is invalid.</param>
/// <param name="Mint">The optional multi-asset minting or burning policy.</param>
/// <param name="ScriptDataHash">The optional hash of the script data (redeemers and datums).</param>
/// <param name="Collateral">The optional collateral inputs for script validation.</param>
/// <param name="RequiredSigners">The optional list of required signer key hashes.</param>
/// <param name="NetworkId">The optional network identifier.</param>
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
        [CborProperty(7)] ReadOnlyMemory<byte>? AuxiliaryDataHash,
        [CborProperty(8)] ulong? ValidityIntervalStart,
        [CborProperty(9)] MultiAssetMint? Mint,
        [CborProperty(11)] ReadOnlyMemory<byte>? ScriptDataHash,
        [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
        [CborProperty(14)] CborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners,
        [CborProperty(15)] int? NetworkId
    ) : TransactionBody, ICborPreserveRaw;

/// <summary>
/// A Babbage-era transaction body with support for reference inputs, collateral return, and total collateral.
/// </summary>
/// <param name="Inputs">The transaction inputs to spend.</param>
/// <param name="Outputs">The transaction outputs.</param>
/// <param name="Fee">The transaction fee in lovelace.</param>
/// <param name="TimeToLive">The optional slot after which the transaction is invalid.</param>
/// <param name="Certificates">The optional list of certificates.</param>
/// <param name="Withdrawals">The optional stake reward withdrawals.</param>
/// <param name="Update">The optional protocol parameter update proposal.</param>
/// <param name="AuxiliaryDataHash">The optional hash of the auxiliary data.</param>
/// <param name="ValidityIntervalStart">The optional slot before which the transaction is invalid.</param>
/// <param name="Mint">The optional multi-asset minting or burning policy.</param>
/// <param name="ScriptDataHash">The optional hash of the script data.</param>
/// <param name="Collateral">The optional collateral inputs for script validation.</param>
/// <param name="RequiredSigners">The optional list of required signer key hashes.</param>
/// <param name="NetworkId">The optional network identifier.</param>
/// <param name="CollateralReturn">The optional collateral return output.</param>
/// <param name="TotalCollateral">The optional total collateral amount in lovelace.</param>
/// <param name="ReferenceInputs">The optional reference inputs (read-only, not consumed).</param>
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
    [CborProperty(7)] ReadOnlyMemory<byte>? AuxiliaryDataHash,
    [CborProperty(8)] ulong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] ReadOnlyMemory<byte>? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners,
    [CborProperty(15)] int? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] ulong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs
) : TransactionBody, ICborPreserveRaw;

/// <summary>
/// A Conway-era transaction body with governance features including voting procedures, proposals, and donations.
/// </summary>
/// <param name="Inputs">The transaction inputs to spend.</param>
/// <param name="Outputs">The transaction outputs.</param>
/// <param name="Fee">The transaction fee in lovelace.</param>
/// <param name="TimeToLive">The optional slot after which the transaction is invalid.</param>
/// <param name="Certificates">The optional list of certificates.</param>
/// <param name="Withdrawals">The optional stake reward withdrawals.</param>
/// <param name="AuxiliaryDataHash">The optional hash of the auxiliary data.</param>
/// <param name="ValidityIntervalStart">The optional slot before which the transaction is invalid.</param>
/// <param name="Mint">The optional multi-asset minting or burning policy.</param>
/// <param name="ScriptDataHash">The optional hash of the script data.</param>
/// <param name="Collateral">The optional collateral inputs for script validation.</param>
/// <param name="RequiredSigners">The optional list of required signer key hashes.</param>
/// <param name="NetworkId">The optional network identifier.</param>
/// <param name="CollateralReturn">The optional collateral return output.</param>
/// <param name="TotalCollateral">The optional total collateral amount in lovelace.</param>
/// <param name="ReferenceInputs">The optional reference inputs (read-only, not consumed).</param>
/// <param name="VotingProcedures">The optional governance voting procedures.</param>
/// <param name="ProposalProcedures">The optional governance proposal procedures.</param>
/// <param name="TreasuryValue">The optional current treasury value.</param>
/// <param name="Donation">The optional donation to the treasury.</param>
[CborSerializable]
[CborMap]
public partial record ConwayTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] ulong Fee,
    [CborProperty(3)] ulong? TimeToLive,
    [CborProperty(4)] CborMaybeIndefList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(7)] ReadOnlyMemory<byte>? AuxiliaryDataHash,
    [CborProperty(8)] ulong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] ReadOnlyMemory<byte>? ScriptDataHash,
    [CborProperty(13)] CborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] CborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners,
    [CborProperty(15)] int? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] ulong? TotalCollateral,
    [CborProperty(18)] CborMaybeIndefList<TransactionInput>? ReferenceInputs,
    [CborProperty(19)] VotingProcedures? VotingProcedures,
    [CborProperty(20)] CborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
    [CborProperty(21)] ulong? TreasuryValue,
    [CborProperty(22)] ulong? Donation
) : TransactionBody, ICborPreserveRaw;
