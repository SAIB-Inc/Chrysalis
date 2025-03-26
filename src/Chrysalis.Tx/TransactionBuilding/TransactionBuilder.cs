using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Tx.TransactionBuilding;

public class TransactionBuilder
{
    public ConwayTransactionBody body;
    public PostAlonzoTransactionWitnessSet witnessSet;
    public ConwayProtocolParamUpdate? pparams;
    public readonly TransactionBodyBuilder bodyBuilder = new();
    public readonly WitnessSetBuilder witnessBuilder = new();
    private AuxiliaryData auxiliaryData;

    public TransactionBuilder() { 
         body = default!;
         witnessSet = default!;
         auxiliaryData = default!;
    }
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

    public TransactionBuilder AddOutput(TransactionOutput output, bool isChange = false)
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

    public TransactionBuilder AddRequiredSigner(byte[] signer)
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

    public TransactionBuilder AddPlutusV1Script(byte[] script)
    {
        witnessBuilder.AddPlutusV1Script(script);
        return this;
    }

    public TransactionBuilder AddPlutusV2Script(byte[] script)
    {
        witnessBuilder.AddPlutusV2Script(script);
        return this;
    }

    public TransactionBuilder AddPlutusV3Script(byte[] script)
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
        body = bodyBuilder.Build();
        witnessSet = witnessBuilder.Build();
        return new PostMaryTransaction(body, witnessSet, true, auxiliaryData);
    }

    internal void SetRedeemers(RedeemerMap redeemerMap)
    {
        throw new NotImplementedException();
    }
}