using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using CProposalProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.ProposalProcedure;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CCertificate = Chrysalis.Cbor.Types.Cardano.Core.Certificates.Certificate;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using NSec.Cryptography;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

public static class TransactionBodyExtensions
{
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Inputs.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Inputs.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Inputs.GetValue(),
            _ => []
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Outputs.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Outputs.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Outputs.GetValue(),
            _ => []
        };

    public static ulong Fee(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Fee,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Fee,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Fee,
            _ => default
        };

    public static ulong? ValidFrom(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.ValidityIntervalStart,
            BabbageTransactionBody babbageTxBody => babbageTxBody.ValidityIntervalStart,
            ConwayTransactionBody conwayTxBody => conwayTxBody.ValidityIntervalStart,
            _ => null
        };

    public static ulong? ValidTo(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.TimeToLive,
            BabbageTransactionBody babbageTxBody => babbageTxBody.TimeToLive,
            ConwayTransactionBody conwayTxBody => conwayTxBody.TimeToLive,
            _ => null
        };
    
    public static IEnumerable<CCertificate>? Certificates(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Certificates?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Certificates?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Certificates?.GetValue(),
            _ => null
        };

    public static Dictionary<RewardAccount, ulong>? Withdrawals(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Withdrawals?.Value,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Withdrawals?.Value,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Withdrawals?.Value,
            _ => null
        };

    public static byte[]? AuxiliaryDataHash(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.AuxiliaryDataHash,
            BabbageTransactionBody babbageTxBody => babbageTxBody.AuxiliaryDataHash,
            ConwayTransactionBody conwayTxBody => conwayTxBody.AuxiliaryDataHash,
            _ => null
        };

    public static Dictionary<byte[], TokenBundleMint>? Mint(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Mint?.Value,
            BabbageTransactionBody babbageTxBody => babbageTxBody.Mint?.Value,
            ConwayTransactionBody conwayTxBody => conwayTxBody.Mint?.Value,
            _ => null
        };

    public static byte[]? ScriptDataHash(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.ScriptDataHash,
            BabbageTransactionBody babbageTxBody => babbageTxBody.ScriptDataHash,
            ConwayTransactionBody conwayTxBody => conwayTxBody.ScriptDataHash,
            _ => null
        };

    public static IEnumerable<TransactionInput>? Collateral(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Collateral?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.Collateral?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.Collateral?.GetValue(),
            _ => null
        };

    public static IEnumerable<byte[]>? RequiredSigners(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.RequiredSigners?.GetValue(),
            BabbageTransactionBody babbageTxBody => babbageTxBody.RequiredSigners?.GetValue(),
            ConwayTransactionBody conwayTxBody => conwayTxBody.RequiredSigners?.GetValue(),
            _ => null
        };

    public static int? NetworkId(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.NetworkId,
            ConwayTransactionBody conwayTxBody => conwayTxBody.NetworkId,
            _ => null
        };

    public static TransactionOutput? CollateralChange(this TransactionBody self) =>
        self switch
        {
            BabbageTransactionBody babbageTxBody => babbageTxBody.CollateralReturn,
            ConwayTransactionBody conwayTxBody => conwayTxBody.CollateralReturn,
            _ => null
        };

    public static ulong? TotalCollateral(this TransactionBody self) =>
        self switch
        {
            BabbageTransactionBody babbageTxBody => babbageTxBody.TotalCollateral,
            ConwayTransactionBody conwayTxBody => conwayTxBody.TotalCollateral,
            _ => null
        };

    public static IEnumerable<TransactionInput>? ReferenceInputs(this TransactionBody self) =>
        self switch
        {
                BabbageTransactionBody babbageTxBody => babbageTxBody.ReferenceInputs?.GetValue(),
                ConwayTransactionBody conwayTxBody => conwayTxBody.ReferenceInputs?.GetValue(),
                _ => null
        };

    public static Dictionary<Voter, GovActionIdVotingProcedure>? VotingProcedures(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.VotingProcedures?.Value,
            _ => null
        };

    public static IEnumerable<CProposalProcedure>? ProposalProcedures(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.ProposalProcedures?.GetValue(),
            _ => null
        };

    public static ulong? TreasuryValue(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.TreasuryValue,
            _ => null
        };

    public static ulong? Donation(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.Donation,
            _ => null
        };

    public static string Hash(this TransactionBody self)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        byte[] raw = self.Raw is null ? CborSerializer.Serialize(self) : self.Raw.Value.ToArray();
        return Convert.ToHexString(algorithm.Hash(raw));
    }
}