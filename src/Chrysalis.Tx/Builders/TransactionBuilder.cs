using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Builders;

public class TransactionBuilder
{
    public ConwayTransactionBody body;
    public PostAlonzoTransactionWitnessSet witnessSet;
    public ConwayProtocolParamUpdate? pparams;
    public TransactionOutput? changeOutput;
    private AuxiliaryData? auxiliaryData;

    public TransactionBuilder()
    {
        body = CborTypeDefaults.TransactionBody;
        witnessSet = CborTypeDefaults.TransactionWitnessSet;
        auxiliaryData = null;
    }
    public static TransactionBuilder Create(ConwayProtocolParamUpdate pparams)
    {
        return new TransactionBuilder() { pparams = pparams };
    }

    #region Transaction Body Methods

    public TransactionBuilder SetNetworkId(int networkId)
    {
        body = body with { NetworkId = networkId };
        return this;
    }

    public TransactionBuilder AddInput(TransactionInput input)
    {
        body = body with { Inputs = new CborDefListWithTag<TransactionInput>([.. body.Inputs.Value(), input]) };
        return this;
    }

    public TransactionBuilder SetInputs(List<TransactionInput> inputs)
    {
        body = body with { Inputs = new CborDefListWithTag<TransactionInput>(inputs) };
        return this;
    }

    public TransactionBuilder AddOutput(TransactionOutput output, bool isChange = false)
    {
        if (isChange)
        {
            changeOutput = output;
        }

        body = body with { Outputs = new CborDefList<TransactionOutput>([.. body.Outputs.Value(), output]) };

        return this;
    }

    public TransactionBuilder SetOutputs(List<TransactionOutput> outputs)
    {
        body = body with { Outputs = new CborDefList<TransactionOutput>(outputs) };
        return this;
    }

    public TransactionBuilder SetFee(ulong feeAmount)
    {
        body = body with { Fee = feeAmount };
        return this;
    }

    public TransactionBuilder SetTtl(ulong ttl)
    {
        body = body with { TimeToLive = ttl };
        return this;
    }
    public TransactionBuilder SetValidityIntervalStart(ulong validityStart)
    {
        body = body with { ValidityIntervalStart = validityStart };
        return this;
    }

    public TransactionBuilder AddCertificate(Certificate certificate)
    {
        body = body with { Certificates = new CborDefListWithTag<Certificate>([.. body.Certificates.Value(), certificate]) };
        return this;
    }

    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        body = body with { Withdrawals = withdrawals };
        return this;
    }

    public TransactionBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        body = body with { AuxiliaryDataHash = hash };
        return this;
    }

    public TransactionBuilder AddMint(MultiAssetMint mint)
    {
        foreach (var asset in mint.Value)
        {
            body = body with { Mint = new MultiAssetMint(new Dictionary<byte[], TokenBundleMint>(mint.Value) { { asset.Key, asset.Value } }) };
        }
        return this;
    }

    public TransactionBuilder SetMint(MultiAssetMint mint)
    {
        body = body with { Mint = mint };
        return this;
    }

    public TransactionBuilder SetScriptDataHash(byte[] hash)
    {
        body = body with { ScriptDataHash = hash };
        return this;
    }

    public TransactionBuilder AddCollateral(TransactionInput collateral)
    {
        body = body with { Collateral = new CborDefListWithTag<TransactionInput>([.. body.Collateral.Value(), collateral]) };
        return this;
    }

    public TransactionBuilder AddRequiredSigner(byte[] signer)
    {
        body = body with { RequiredSigners = new CborDefListWithTag<byte[]>([.. body.RequiredSigners.Value(), signer]) };
        return this;
    }

    public TransactionBuilder SetCollateralReturn(TransactionOutput collateralReturn)
    {
        body = body with { CollateralReturn = collateralReturn };
        return this;
    }

    public TransactionBuilder SetTotalCollateral(ulong totalCollateral)
    {
        body = body with { TotalCollateral = totalCollateral };
        return this;
    }

    public TransactionBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        body = body with { ReferenceInputs = new CborDefListWithTag<TransactionInput>([.. body.ReferenceInputs.Value(), referenceInput]) };
        return this;
    }

    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        body = body with { VotingProcedures = votingProcedures };
        return this;
    }

    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        body = body with { ProposalProcedures = new CborDefListWithTag<ProposalProcedure>([.. body.ProposalProcedures.Value(), proposal]) };
        return this;
    }

    public TransactionBuilder SetTreasuryValue(ulong treasuryValue)
    {
        body = body with { TreasuryValue = treasuryValue };
        return this;
    }

    public TransactionBuilder SetDonation(ulong donation)
    {
        body = body with { Donation = donation };
        return this;
    }

    #endregion

    #region Witness Set Methods

    public TransactionBuilder AddVKeyWitness(VKeyWitness witness)
    {
        witnessSet = witnessSet with { VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>([.. witnessSet.VKeyWitnessSet.Value(), witness]) };
        return this;
    }

    public TransactionBuilder AddNativeScript(NativeScript script)
    {
        witnessSet = witnessSet with { NativeScriptSet = new CborDefListWithTag<NativeScript>([.. witnessSet.NativeScriptSet.Value(), script]) };
        return this;
    }

    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        witnessSet = witnessSet with { BootstrapWitnessSet = new CborDefListWithTag<BootstrapWitness>([.. witnessSet.BootstrapWitnessSet.Value(), witness]) };
        return this;
    }

    public TransactionBuilder AddPlutusV1Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV1ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV1ScriptSet.Value(), script]) };
        return this;
    }

    public TransactionBuilder AddPlutusV2Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV2ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV2ScriptSet.Value(), script]) };
        return this;
    }

    public TransactionBuilder AddPlutusV3Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV3ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV3ScriptSet.Value(), script]) };
        return this;
    }

    public TransactionBuilder AddPlutusData(PlutusData data)
    {
        witnessSet = witnessSet with { PlutusDataSet = new CborDefListWithTag<PlutusData>([.. witnessSet.PlutusDataSet.Value(), data]) };
        return this;
    }

    public TransactionBuilder SetRedeemers(Redeemers redeemers)
    {
        witnessSet = witnessSet with { Redeemers = redeemers };
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
        return new PostMaryTransaction(body, witnessSet, true, auxiliaryData);
    }

}