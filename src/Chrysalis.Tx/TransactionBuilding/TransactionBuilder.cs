using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

namespace Chrysalis.Tx.TransactionBuilding;

public class TransactionBuilder
{
    public ConwayProtocolParamUpdate? pparams;
    public readonly TransactionBodyBuilder bodyBuilder = new();
    public readonly WitnessSetBuilder witnessBuilder = new();
    private AuxiliaryData? auxiliaryData;

    public TransactionBuilder() { }
    public static TransactionBuilder Create() => new();
    public static TransactionBuilder Create(ConwayProtocolParamUpdate pparams)
    {
        return new TransactionBuilder { pparams = pparams };
    }

    #region Transaction Body Methods

    public TransactionBuilder SetNetworkId(int networkId)
    {
        bodyBuilder.SetNetworkId(networkId);
        return this;
    }

    public TransactionBuilder AddInput(TransactionInput input)
    {
        bodyBuilder.AddInput(input);
        return this;
    }

    public TransactionBuilder AddOutput(PostAlonzoTransactionOutput output, bool isChange = false)
    {
        bodyBuilder.AddOutput((output, isChange));
        return this;
    }

    public TransactionBuilder SetFee(ulong feeAmount)
    {
        bodyBuilder.SetFee(feeAmount);
        return this;
    }

    public TransactionBuilder SetTtl(ulong ttl)
    {
        bodyBuilder.SetTtl(ttl);
        return this;
    }
    public TransactionBuilder SetValidityIntervalStart(ulong validityStart)
    {
        bodyBuilder.SetValidityIntervalStart(validityStart);
        return this;
    }

    public TransactionBuilder AddCertificate(Certificate certificate)
    {
        bodyBuilder.AddCertificate(certificate);
        return this;
    }

    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        bodyBuilder.SetWithdrawals(withdrawals);
        return this;
    }

    public TransactionBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        bodyBuilder.SetAuxiliaryDataHash(hash);
        return this;
    }

    public TransactionBuilder SetMint(MultiAssetMint mint)
    {
        bodyBuilder.SetMint(mint);
        return this;
    }

    public TransactionBuilder SetScriptDataHash(byte[] hash)
    {
        bodyBuilder.SetScriptDataHash(hash);
        return this;
    }

    public TransactionBuilder AddCollateral(TransactionInput collateral)
    {
        bodyBuilder.AddCollateral(collateral);
        return this;
    }

    public TransactionBuilder AddRequiredSigner(CborBytes signer)
    {
        bodyBuilder.AddRequiredSigner(signer);
        return this;
    }

    public TransactionBuilder SetCollateralReturn(TransactionOutput collateralReturn)
    {
        bodyBuilder.SetCollateralReturn(collateralReturn);
        return this;
    }

    public TransactionBuilder SetTotalCollateral(ulong totalCollateral)
    {
        bodyBuilder.SetTotalCollateral(totalCollateral);
        return this;
    }

    public TransactionBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        bodyBuilder.AddReferenceInput(referenceInput);
        return this;
    }

    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        bodyBuilder.SetVotingProcedures(votingProcedures);
        return this;
    }

    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        bodyBuilder.AddProposalProcedure(proposal);
        return this;
    }

    public TransactionBuilder SetTreasuryValue(ulong treasuryValue)
    {
        bodyBuilder.SetTreasuryValue(treasuryValue);
        return this;
    }

    public TransactionBuilder SetDonation(ulong donation)
    {
        bodyBuilder.SetDonation(donation);
        return this;
    }

    #endregion

    #region Witness Set Methods

    public TransactionBuilder AddVKeyWitness(VKeyWitness witness)
    {
        witnessBuilder.AddVKeyWitness(witness);
        return this;
    }

    public TransactionBuilder AddNativeScript(NativeScript script)
    {
        witnessBuilder.AddNativeScript(script);
        return this;
    }

    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        witnessBuilder.AddBootstrapWitness(witness);
        return this;
    }

    public TransactionBuilder AddPlutusV1Script(CborBytes script)
    {
        witnessBuilder.AddPlutusV1Script(script);
        return this;
    }

    public TransactionBuilder AddPlutusV2Script(CborBytes script)
    {
        witnessBuilder.AddPlutusV2Script(script);
        return this;
    }

    public TransactionBuilder AddPlutusV3Script(CborBytes script)
    {
        witnessBuilder.AddPlutusV3Script(script);
        return this;
    }

    public TransactionBuilder AddPlutusData(PlutusData data)
    {
        witnessBuilder.AddPlutusData(data);
        return this;
    }

    public TransactionBuilder SetRedeemers(Redeemers redeemers)
    {
        witnessBuilder.SetRedeemers(redeemers);
        return this;
    }

    #endregion

    #region Auxiliary Data

    public TransactionBuilder SetAuxiliaryData(AuxiliaryData data)
    {
        auxiliaryData = data;
        return this;
    }

    #endregion

    public PostMaryTransaction Build()
    {
        var body = bodyBuilder.Build();
        var witnessSet = witnessBuilder.Build();
        return new PostMaryTransaction(body, witnessSet, new CborBool(true), new CborNullable<AuxiliaryData>(auxiliaryData));
    }
}