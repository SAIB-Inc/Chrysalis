using System.Security.Cryptography.X509Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.TransactionBuilding;


public class TransactionBodyBuilder
{
    private CborInt? networkId;
    public List<TransactionInput> Inputs { get; set; } = [];
    public List<(TransactionOutput, bool)> Outputs { get; set; } = [];
    private CborUlong? fee;
    private CborUlong? ttl;
    private CborUlong? validityIntervalStart;
    private readonly List<Certificate> certificates = [];
    private Withdrawals? withdrawals;
    private CborBytes? auxiliaryDataHash;
    private MultiAssetMint? mint;
    private CborBytes? scriptDataHash;
    private readonly List<TransactionInput> collaterals = [];
    private readonly List<CborBytes> requiredSigners = [];
    private TransactionOutput? collateralReturn;
    private CborUlong? totalCollateral;
    private readonly List<TransactionInput> referenceInputs = [];
    private VotingProcedures? votingProcedures;
    private readonly List<ProposalProcedure> proposalProcedures = [];
    private CborUlong? treasuryValue;
    private CborUlong? donation;

    public TransactionBodyBuilder SetNetworkId(int networkId)
    {
        this.networkId = new CborInt(networkId);
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
        fee = new CborUlong(feeAmount);
        return this;
    }

    public TransactionBodyBuilder SetTtl(ulong ttl)
    {
        this.ttl = new CborUlong(ttl);
        return this;
    }

    public TransactionBodyBuilder SetValidityIntervalStart(ulong validityStart)
    {
        validityIntervalStart = new CborUlong(validityStart);
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
        auxiliaryDataHash = new CborBytes(hash);
        return this;
    }

    public TransactionBodyBuilder SetMint(MultiAssetMint mint)
    {
        this.mint = mint;
        return this;
    }

    public TransactionBodyBuilder SetScriptDataHash(byte[] hash)
    {
        scriptDataHash = new CborBytes(hash);
        return this;
    }

    public TransactionBodyBuilder AddCollateral(TransactionInput collateral)
    {
        collaterals.Add(collateral);
        return this;
    }

    public TransactionBodyBuilder AddRequiredSigner(CborBytes signer)
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
        this.totalCollateral = new CborUlong(totalCollateral);
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
        this.treasuryValue = new CborUlong(treasuryValue);
        return this;
    }

    public TransactionBodyBuilder SetDonation(ulong donation)
    {
        this.donation = new CborUlong(donation);
        return this;
    }

    public ConwayTransactionBody Build()
    {
        return new ConwayTransactionBody(
            new CborDefListWithTag<TransactionInput>(Inputs),
            new CborDefList<TransactionOutput>([.. Outputs.Select(o => o.Item1)]),
            fee ?? new CborUlong(0),
            ttl,
            certificates.Count != 0 ? new CborDefList<Certificate>(certificates) : null,
            withdrawals,
            auxiliaryDataHash,
            validityIntervalStart,
            mint,
            scriptDataHash,
            collaterals.Count != 0 ? new CborDefListWithTag<TransactionInput>(collaterals) : null,
            requiredSigners.Count != 0 ? new CborDefList<CborBytes>(requiredSigners) : null,
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