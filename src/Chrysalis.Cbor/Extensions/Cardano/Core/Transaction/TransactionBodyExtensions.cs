using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using CProposalProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.ProposalProcedure;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CCertificate = Chrysalis.Cbor.Types.Cardano.Core.Certificates.Certificate;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Serialization;
using Blake2Fast;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="TransactionBody"/> to access fields across eras.
/// </summary>
public static class TransactionBodyExtensions
{
    /// <summary>
    /// Gets the transaction inputs.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The transaction inputs.</returns>
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Inputs.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Inputs.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Inputs.GetValue(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the transaction outputs.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The transaction outputs.</returns>
    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Outputs.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Outputs.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Outputs.GetValue(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the transaction fee in lovelace.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The fee amount.</returns>
    public static ulong Fee(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Fee,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Fee,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Fee,
            _ => default
        };
    }

    /// <summary>
    /// Gets the validity interval start slot, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The start slot, or null.</returns>
    public static ulong? ValidFrom(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.ValidityIntervalStart,
            BabbageTransactionBody babbageTxBody => babbageTxBody.ValidityIntervalStart,
            ConwayTransactionBody conwayTxBody => conwayTxBody.ValidityIntervalStart,
            _ => null
        };
    }

    /// <summary>
    /// Gets the time-to-live (expiry) slot, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The TTL slot, or null.</returns>
    public static ulong? ValidTo(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.TimeToLive,
            BabbageTransactionBody babbageTxBody => babbageTxBody.TimeToLive,
            ConwayTransactionBody conwayTxBody => conwayTxBody.TimeToLive,
            _ => null
        };
    }

    /// <summary>
    /// Gets the certificates from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The certificates, or null.</returns>
    public static IEnumerable<CCertificate>? Certificates(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Certificates?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Certificates?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Certificates?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the withdrawals from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The withdrawals dictionary, or null.</returns>
    public static Dictionary<RewardAccount, ulong>? Withdrawals(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Withdrawals?.Value,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Withdrawals?.Value,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Withdrawals?.Value,
            _ => null
        };
    }

    /// <summary>
    /// Gets the auxiliary data hash from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The auxiliary data hash bytes, or null.</returns>
    public static byte[]? AuxiliaryDataHash(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.AuxiliaryDataHash,
            BabbageTransactionBody babbageTxBody => babbageTxBody.AuxiliaryDataHash,
            ConwayTransactionBody conwayTxBody => conwayTxBody.AuxiliaryDataHash,
            _ => null
        };
    }

    /// <summary>
    /// Gets the mint field from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The mint dictionary, or null.</returns>
    public static Dictionary<byte[], TokenBundleMint>? Mint(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Mint?.Value,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Mint?.Value,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Mint?.Value,
            _ => null
        };
    }

    /// <summary>
    /// Gets the script data hash from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The script data hash bytes, or null.</returns>
    public static byte[]? ScriptDataHash(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.ScriptDataHash,
            BabbageTransactionBody babbageTxBody => babbageTxBody.ScriptDataHash,
            ConwayTransactionBody conwayTxBody => conwayTxBody.ScriptDataHash,
            _ => null
        };
    }

    /// <summary>
    /// Gets the collateral inputs from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The collateral inputs, or null.</returns>
    public static IEnumerable<TransactionInput>? Collateral(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Collateral?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Collateral?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Collateral?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the required signers from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The required signer key hashes, or null.</returns>
    public static IEnumerable<byte[]>? RequiredSigners(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.RequiredSigners?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.RequiredSigners?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.RequiredSigners?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the network ID from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The network ID, or null.</returns>
    public static int? NetworkId(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.NetworkId,
            ConwayTransactionBody conwayTxBody => conwayTxBody.NetworkId,
            _ => null
        };
    }

    /// <summary>
    /// Gets the collateral return output from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The collateral return output, or null.</returns>
    public static TransactionOutput? CollateralChange(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageTransactionBody babbageTxBody => babbageTxBody.CollateralReturn,
            ConwayTransactionBody conwayTxBody => conwayTxBody.CollateralReturn,
            _ => null
        };
    }

    /// <summary>
    /// Gets the total collateral amount from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The total collateral in lovelace, or null.</returns>
    public static ulong? TotalCollateral(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageTransactionBody babbageTxBody => babbageTxBody.TotalCollateral,
            ConwayTransactionBody conwayTxBody => conwayTxBody.TotalCollateral,
            _ => null
        };
    }

    /// <summary>
    /// Gets the reference inputs from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The reference inputs, or null.</returns>
    public static IEnumerable<TransactionInput>? ReferenceInputs(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            BabbageTransactionBody babbageTxBody => babbageTxBody.ReferenceInputs?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.ReferenceInputs?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the voting procedures from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The voting procedures dictionary, or null.</returns>
    public static Dictionary<Voter, GovActionIdVotingProcedure>? VotingProcedures(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.VotingProcedures?.Value,
            _ => null
        };
    }

    /// <summary>
    /// Gets the proposal procedures from the transaction body, if any.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The proposal procedures, or null.</returns>
    public static IEnumerable<CProposalProcedure>? ProposalProcedures(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.ProposalProcedures?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the treasury value from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The treasury value in lovelace, or null.</returns>
    public static ulong? TreasuryValue(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.TreasuryValue,
            _ => null
        };
    }

    /// <summary>
    /// Gets the donation amount from the transaction body, if set.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The donation in lovelace, or null.</returns>
    public static ulong? Donation(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.Donation,
            _ => null
        };
    }

    /// <summary>
    /// Computes the Blake2b-256 hash of the transaction body as a hex string.
    /// </summary>
    /// <param name="self">The transaction body instance.</param>
    /// <returns>The hex-encoded transaction hash.</returns>
    public static string Hash(this TransactionBody self)
    {
        ArgumentNullException.ThrowIfNull(self);
        byte[] raw = self.Raw is null ? CborSerializer.Serialize(self) : self.Raw.Value.ToArray();
        return Convert.ToHexString(Blake2b.HashData(32, raw)).ToUpperInvariant();
    }
}
