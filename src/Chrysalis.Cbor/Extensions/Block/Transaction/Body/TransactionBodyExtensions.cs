using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using CCertificate = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates.Certificate;
using CVotingProcedure = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance.VotingProcedure;
using CProposalProcedure = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance.ProposalProcedure;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.TransactionBody;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TokenBundle;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body;

public static class TransactionBodyExtensions
{
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Inputs switch
            {
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Inputs switch
            {
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Inputs switch
            {
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            _ => []
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.Outputs switch
            {
                CborMaybeIndefList<TransactionOutput.AlonzoTransactionOutput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionOutput.AlonzoTransactionOutput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionOutput.AlonzoTransactionOutput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionOutput.AlonzoTransactionOutput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Outputs switch
            {
                CborMaybeIndefList<TransactionOutput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionOutput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionOutput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionOutput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Outputs switch
            {
                CborMaybeIndefList<TransactionOutput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionOutput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionOutput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionOutput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
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
                CborMaybeIndefList<CCertificate>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CCertificate>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CCertificate>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CCertificate>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Certificates switch
            {
                CborMaybeIndefList<CCertificate>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CCertificate>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CCertificate>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CCertificate>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Certificates switch
            {
                CborMaybeIndefList<CCertificate>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CCertificate>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CCertificate>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CCertificate>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
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
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.Collateral switch
            {
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.Collateral switch
            {
                CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? RequiredSigners(this TransactionBody self) =>
        self switch
        {
            AlonzoTransactionBody alonzoTxBody => alonzoTxBody.RequiredSigners switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageTransactionBody babbageTxBody => babbageTxBody.RequiredSigners switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayTransactionBody conwayTxBody => conwayTxBody.RequiredSigners switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
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
                    CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                    CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                    CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                    CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                    _ => null
                },
                ConwayTransactionBody conwayTxBody => conwayTxBody.ReferenceInputs switch
                {
                    CborMaybeIndefList<TransactionInput>.CborDefList defList => defList.Value,
                    CborMaybeIndefList<TransactionInput>.CborIndefList indefList => indefList.Value,
                    CborMaybeIndefList<TransactionInput>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                    CborMaybeIndefList<TransactionInput>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                    _ => null
                },
                _ => null
        };

    public static Dictionary<Voter, Dictionary<GovActionId, CVotingProcedure>>? VotingProcedures(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.VotingProcedures.Value,
            _ => null
        };

    public static IEnumerable<CProposalProcedure>? ProposalProcedures(this TransactionBody self) =>
        self switch
        {
            ConwayTransactionBody conwayTxBody => conwayTxBody.ProposalProcedures switch
            {
                CborMaybeIndefList<CProposalProcedure>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CProposalProcedure>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CProposalProcedure>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CProposalProcedure>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
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
