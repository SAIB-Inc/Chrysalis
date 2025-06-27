using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types;
using Chrysalis.Tx.Utils;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Network.Cbor.LocalStateQuery;

namespace Chrysalis.Tx.Builders;

public class TransactionBuilder
{
    public ConwayTransactionBody body;
    public PostAlonzoTransactionWitnessSet witnessSet;
    public ProtocolParams? pparams;
    public TransactionOutput? changeOutput;
    private PostAlonzoAuxiliaryDataMap? auxiliaryData;

    public TransactionBuilder()
    {
        body = CborTypeDefaults.TransactionBody;
        witnessSet = CborTypeDefaults.TransactionWitnessSet;
        auxiliaryData = null;
    }
    public static TransactionBuilder Create(ProtocolParams pparams)
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
        body = body with { Inputs = new CborDefListWithTag<TransactionInput>([.. body.Inputs.GetValue(), input]) };
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

        body = body with { Outputs = new CborDefList<TransactionOutput>([.. body.Outputs.GetValue(), output]) };

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
        body = body with { Certificates = new CborDefListWithTag<Certificate>([.. body.Certificates is not null ? body.Certificates.GetValue() : [], certificate]) };
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
        if (body.Mint == null)
        {
            body = body with { Mint = mint };
            return this;
        }
        MultiAssetMint mergedMint = MergeMints(body.Mint, mint);

        body = body with { Mint = mergedMint };
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
        body = body with { Collateral = new CborDefListWithTag<TransactionInput>([.. body.Collateral is not null ? body.Collateral.GetValue() : [], collateral]) };
        return this;
    }

    public TransactionBuilder AddRequiredSigner(byte[] signer)
    {
        body = body with { RequiredSigners = new CborDefListWithTag<byte[]>([.. body.RequiredSigners is not null ? body.RequiredSigners.GetValue() : [], signer]) };
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
        body = body with { ReferenceInputs = new CborDefListWithTag<TransactionInput>([.. body.ReferenceInputs is not null ? body.ReferenceInputs.GetValue() : [], referenceInput]) };
        return this;
    }

    public TransactionBuilder SetReferenceInputs(List<TransactionInput> referenceInputs)
    {
        body = body with { ReferenceInputs = new CborDefListWithTag<TransactionInput>(referenceInputs) };
        return this;
    }

    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        body = body with { VotingProcedures = votingProcedures };
        return this;
    }

    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        body = body with { ProposalProcedures = new CborDefListWithTag<ProposalProcedure>([.. body.ProposalProcedures is not null ? body.ProposalProcedures.GetValue() : [], proposal]) };
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
        witnessSet = witnessSet with { VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>([.. witnessSet.VKeyWitnessSet is not null ? witnessSet.VKeyWitnessSet.GetValue() : [], witness]) };
        return this;
    }

    public TransactionBuilder AddNativeScript(NativeScript script)
    {
        witnessSet = witnessSet with { NativeScriptSet = new CborDefListWithTag<NativeScript>([.. witnessSet.NativeScriptSet is not null ? witnessSet.NativeScriptSet.GetValue() : [], script]) };
        return this;
    }

    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        witnessSet = witnessSet with { BootstrapWitnessSet = new CborDefListWithTag<BootstrapWitness>([.. witnessSet.BootstrapWitnessSet is not null ? witnessSet.BootstrapWitnessSet.GetValue() : [], witness]) };
        return this;
    }

    public TransactionBuilder AddPlutusV1Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV1ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV1ScriptSet is not null ? witnessSet.PlutusV1ScriptSet.GetValue() : [], script]) };
        return this;
    }

    public TransactionBuilder AddPlutusV2Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV2ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV2ScriptSet is not null ? witnessSet.PlutusV2ScriptSet.GetValue() : [], script]) };
        return this;
    }

    public TransactionBuilder AddPlutusV3Script(byte[] script)
    {
        witnessSet = witnessSet with { PlutusV3ScriptSet = new CborDefListWithTag<byte[]>([.. witnessSet.PlutusV3ScriptSet is not null ? witnessSet.PlutusV3ScriptSet.GetValue() : [], script]) };
        return this;
    }

    public TransactionBuilder AddPlutusData(PlutusData data)
    {
        witnessSet = witnessSet with { PlutusDataSet = new CborDefListWithTag<PlutusData>([.. witnessSet.PlutusDataSet is not null ? witnessSet.PlutusDataSet.GetValue() : [], data]) };
        return this;
    }

    public TransactionBuilder SetRedeemers(Redeemers redeemers)
    {
        witnessSet = witnessSet with { Redeemers = redeemers };
        return this;
    }

    #endregion

    #region Auxiliary Data

    public TransactionBuilder SetAuxiliaryData(PostAlonzoAuxiliaryDataMap data)
    {
        auxiliaryData = data;
        return this;
    }

    public TransactionBuilder SetMetadata(Metadata metadata)
    {
        if (auxiliaryData == null)
        {
            auxiliaryData = new PostAlonzoAuxiliaryDataMap(metadata, null, null, null, null);
        }
        else
        {
            auxiliaryData = auxiliaryData with { MetadataValue = metadata };
        }

        return this;
    }

    #endregion

    public PostMaryTransaction Build()
    {
        return new PostMaryTransaction(body, witnessSet, true, auxiliaryData);
    }

    private static MultiAssetMint MergeMints(MultiAssetMint existingMint, MultiAssetMint newMint)
    {
        // Use the custom comparer instead of default dictionary
        Dictionary<byte[], TokenBundleMint> result = new(ByteArrayEqualityComparer.Instance);

        // Copy existing mints
        foreach (var policyEntry in existingMint.Value)
        {
            result[policyEntry.Key] = policyEntry.Value;
        }

        // Merge new mints
        foreach (var policyEntry in newMint.Value)
        {
            // BEFORE: This used nested loops with SequenceEqual
            // AFTER: Direct dictionary lookup with custom comparer
            if (result.TryGetValue(policyEntry.Key, out var existingTokenBundle))
            {
                // Merge token bundles using the same pattern
                var mergedTokens = new Dictionary<byte[], long>(ByteArrayEqualityComparer.Instance);

                // Copy existing tokens
                foreach (var token in existingTokenBundle.Value)
                {
                    mergedTokens[token.Key] = token.Value;
                }

                // Add new tokens
                foreach (var tokenEntry in policyEntry.Value.Value)
                {
                    if (mergedTokens.TryGetValue(tokenEntry.Key, out var existingAmount))
                    {
                        mergedTokens[tokenEntry.Key] = existingAmount + tokenEntry.Value;
                    }
                    else
                    {
                        mergedTokens[tokenEntry.Key] = tokenEntry.Value;
                    }
                }

                result[policyEntry.Key] = new TokenBundleMint(mergedTokens);
            }
            else
            {
                result[policyEntry.Key] = policyEntry.Value;
            }
        }

        return new MultiAssetMint(result);
    }

}