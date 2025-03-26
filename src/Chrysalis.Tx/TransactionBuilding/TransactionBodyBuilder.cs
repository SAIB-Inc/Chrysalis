using System.Security.Cryptography.X509Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Tx.TransactionBuilding;


public class TransactionBodyBuilder
{
    private int? networkId;
    public List<TransactionInput> Inputs { get; set; } = [];
    public List<(TransactionOutput, bool)> Outputs { get; set; } = [];
    private ulong? fee;
    private ulong? ttl;
    private ulong? validityIntervalStart;
    private readonly List<Certificate> certificates = [];
    private Withdrawals? withdrawals;
    private byte[]? auxiliaryDataHash;
    private MultiAssetMint? mint;
    private byte[]? scriptDataHash;
    public readonly List<TransactionInput> collaterals = [];
    private readonly List<byte[]> requiredSigners = [];
    public TransactionOutput? collateralReturn;
    public ulong? totalCollateral;
    private readonly List<TransactionInput> referenceInputs = [];
    private VotingProcedures? votingProcedures;
    private readonly List<ProposalProcedure> proposalProcedures = [];
    private ulong? treasuryValue;
    private ulong? donation;

    public TransactionBodyBuilder SetNetworkId(int networkId)
    {
        this.networkId = networkId;
        return this;
    }

    public TransactionBodyBuilder AddInput(TransactionInput input)
    {
        Inputs.Add(input);
        return this;
    }

    public TransactionBodyBuilder AddOutput((TransactionOutput, bool) output)
    {
        Outputs.Add(output);
        return this;
    }

    public TransactionBodyBuilder SetFee(ulong feeAmount)
    {
        fee = feeAmount;
        return this;
    }

    public TransactionBodyBuilder SetTtl(ulong ttl)
    {
        this.ttl = ttl;
        return this;
    }

    public TransactionBodyBuilder SetValidityIntervalStart(ulong validityStart)
    {
        validityIntervalStart = validityStart;
        return this;
    }

    public TransactionBodyBuilder AddCertificate(Certificate certificate)
    {
        certificates.Add(certificate);
        return this;
    }

    public TransactionBodyBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        this.withdrawals = withdrawals;
        return this;
    }

    public TransactionBodyBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        auxiliaryDataHash = hash;
        return this;
    }

    public TransactionBodyBuilder SetMint(MultiAssetMint mint)
    {
        this.mint = mint;
        return this;
    }

    public TransactionBodyBuilder SetScriptDataHash(byte[] hash)
    {
        scriptDataHash = hash;
        return this;
    }

    public TransactionBodyBuilder AddCollateral(TransactionInput collateral)
    {
        collaterals.Add(collateral);
        return this;
    }

    public TransactionBodyBuilder AddRequiredSigner(byte[] signer)
    {
        requiredSigners.Add(signer);
        return this;
    }

    public TransactionBodyBuilder SetCollateralReturn(TransactionOutput collateralReturn)
    {
        this.collateralReturn = collateralReturn;
        return this;
    }

    public TransactionBodyBuilder SetTotalCollateral(ulong totalCollateral)
    {
        this.totalCollateral = totalCollateral;
        return this;
    }

    public TransactionBodyBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        referenceInputs.Add(referenceInput);
        return this;
    }

    public TransactionBodyBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        this.votingProcedures = votingProcedures;
        return this;
    }

    public TransactionBodyBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        proposalProcedures.Add(proposal);
        return this;
    }

    public TransactionBodyBuilder SetTreasuryValue(ulong treasuryValue)
    {
        this.treasuryValue = treasuryValue;
        return this;
    }

    public TransactionBodyBuilder SetDonation(ulong donation)
    {
        this.donation = donation;
        return this;
    }

    public ConwayTransactionBody Build()
    {
        return new ConwayTransactionBody(
            new CborDefListWithTag<TransactionInput>(Inputs),
            new CborDefList<TransactionOutput>([.. Outputs.Select(o => o.Item1)]),
            fee ?? 0,
            ttl,
            certificates.Count != 0 ? new CborDefList<Certificate>(certificates) : null,
            withdrawals,
            auxiliaryDataHash,
            validityIntervalStart,
            mint,
            scriptDataHash,
            collaterals.Count != 0 ? new CborDefListWithTag<TransactionInput>(collaterals) : null,
            requiredSigners.Count != 0 ? new CborDefList<byte[]>(requiredSigners) : null,
            networkId,
            collateralReturn,
            totalCollateral,
            referenceInputs.Count != 0 ? new CborDefListWithTag<TransactionInput>(referenceInputs) : null,
            votingProcedures,
            proposalProcedures.Any() ? new CborDefList<ProposalProcedure>(proposalProcedures) : null,
            treasuryValue,
            donation
        );
    }
}