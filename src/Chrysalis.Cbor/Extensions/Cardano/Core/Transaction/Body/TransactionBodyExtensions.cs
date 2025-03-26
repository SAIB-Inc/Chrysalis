using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using CProposalProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.ProposalProcedure;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CCertificate = Chrysalis.Cbor.Types.Cardano.Core.Certificates.Certificate;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body;

public static class TransactionBodyExtensions
{
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Inputs switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Inputs switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Inputs switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            _ => []
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Outputs switch
            {
                CborDefList<AlonzoTransactionOutput> defList => defList.Value,
                CborIndefList<AlonzoTransactionOutput> indefList => indefList.Value,
                CborDefListWithTag<AlonzoTransactionOutput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<AlonzoTransactionOutput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Outputs switch
            {
                CborDefList<TransactionOutput> defList => defList.Value,
                CborIndefList<TransactionOutput> indefList => indefList.Value,
                CborDefListWithTag<TransactionOutput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionOutput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Outputs switch
            {
                CborDefList<TransactionOutput> defList => defList.Value,
                CborIndefList<TransactionOutput> indefList => indefList.Value,
                CborDefListWithTag<TransactionOutput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionOutput> indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
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
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Certificates switch
            {
                CborDefList<CCertificate> defList => defList.Value,
                CborIndefList<CCertificate> indefList => indefList.Value,
                CborDefListWithTag<CCertificate> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CCertificate> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Certificates switch
            {
                CborDefList<CCertificate> defList => defList.Value,
                CborIndefList<CCertificate> indefList => indefList.Value,
                CborDefListWithTag<CCertificate> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CCertificate> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Certificates switch
            {
                CborDefList<CCertificate> defList => defList.Value,
                CborIndefList<CCertificate> indefList => indefList.Value,
                CborDefListWithTag<CCertificate> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CCertificate> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
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
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Collateral switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Collateral switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Collateral switch
            {
                CborDefList<TransactionInput> defList => defList.Value,
                CborIndefList<TransactionInput> indefList => indefList.Value,
                CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? RequiredSigners(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.RequiredSigners switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.RequiredSigners switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.RequiredSigners switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
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
                BabbageTransactionBody babbageTxBody => babbageTxBody.ReferenceInputs switch
                {
                    CborDefList<TransactionInput> defList => defList.Value,
                    CborIndefList<TransactionInput> indefList => indefList.Value,
                    CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                    CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                    _ => null
                },
                ConwayTransactionBody conwayTxBody => conwayTxBody.ReferenceInputs switch
                {
                    CborDefList<TransactionInput> defList => defList.Value,
                    CborIndefList<TransactionInput> indefList => indefList.Value,
                    CborDefListWithTag<TransactionInput> defListWithTag => defListWithTag.Value,
                    CborIndefListWithTag<TransactionInput> indefListWithTag => indefListWithTag.Value,
                    _ => null
                },
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
            ConwayTransactionBody conwayTxBody => conwayTxBody.ProposalProcedures switch
            {
                CborDefList<CProposalProcedure> defList => defList.Value,
                CborIndefList<CProposalProcedure> indefList => indefList.Value,
                CborDefListWithTag<CProposalProcedure> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CProposalProcedure> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
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
}