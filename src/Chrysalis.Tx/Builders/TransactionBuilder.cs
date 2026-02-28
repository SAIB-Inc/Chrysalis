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

/// <summary>
/// Fluent builder for constructing Cardano transactions.
/// </summary>
public class TransactionBuilder
{
    /// <summary>Gets or sets the transaction body being built.</summary>
    public ConwayTransactionBody Body { get; set; }

    /// <summary>Gets or sets the transaction witness set.</summary>
    public PostAlonzoTransactionWitnessSet WitnessSet { get; set; }

    /// <summary>Gets or sets the protocol parameters used for fee calculation.</summary>
    public ProtocolParams? Pparams { get; set; }

    /// <summary>Gets or sets the change output for fee adjustment.</summary>
    public TransactionOutput? ChangeOutput { get; set; }

    private PostAlonzoAuxiliaryDataMap? _auxiliaryData;

    /// <summary>
    /// Initializes a new TransactionBuilder with default empty body and witness set.
    /// </summary>
    public TransactionBuilder()
    {
        Body = CborTypeDefaults.TransactionBody;
        WitnessSet = CborTypeDefaults.TransactionWitnessSet;
        _auxiliaryData = null;
    }

    /// <summary>
    /// Creates a new TransactionBuilder with the given protocol parameters.
    /// </summary>
    /// <param name="pparams">The protocol parameters.</param>
    /// <returns>A new TransactionBuilder instance.</returns>
    public static TransactionBuilder Create(ProtocolParams pparams)
    {
        return new TransactionBuilder() { Pparams = pparams };
    }

    #region Transaction Body Methods

    /// <summary>
    /// Sets the network ID on the transaction body.
    /// </summary>
    /// <param name="networkId">The network ID (0 for testnet, 1 for mainnet).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetNetworkId(int networkId)
    {
        Body = Body with { NetworkId = networkId };
        return this;
    }

    /// <summary>
    /// Adds a transaction input to the body.
    /// </summary>
    /// <param name="input">The transaction input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddInput(TransactionInput input)
    {
        Body = Body with { Inputs = new CborDefListWithTag<TransactionInput>([.. Body.Inputs.GetValue(), input]) };
        return this;
    }

    /// <summary>
    /// Replaces all transaction inputs.
    /// </summary>
    /// <param name="inputs">The inputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetInputs(List<TransactionInput> inputs)
    {
        Body = Body with { Inputs = new CborDefListWithTag<TransactionInput>(inputs) };
        return this;
    }

    /// <summary>
    /// Adds a transaction output, optionally marking it as the change output.
    /// </summary>
    /// <param name="output">The transaction output to add.</param>
    /// <param name="isChange">Whether this is the change output for fee adjustment.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddOutput(TransactionOutput output, bool isChange = false)
    {
        if (isChange)
        {
            ChangeOutput = output;
        }

        Body = Body with { Outputs = new CborDefList<TransactionOutput>([.. Body.Outputs.GetValue(), output]) };

        return this;
    }

    /// <summary>
    /// Replaces all transaction outputs.
    /// </summary>
    /// <param name="outputs">The outputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetOutputs(List<TransactionOutput> outputs)
    {
        Body = Body with { Outputs = new CborDefList<TransactionOutput>(outputs) };
        return this;
    }

    /// <summary>
    /// Sets the transaction fee.
    /// </summary>
    /// <param name="feeAmount">The fee in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetFee(ulong feeAmount)
    {
        Body = Body with { Fee = feeAmount };
        return this;
    }

    /// <summary>
    /// Sets the transaction time-to-live (TTL).
    /// </summary>
    /// <param name="ttl">The TTL slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTtl(ulong ttl)
    {
        Body = Body with { TimeToLive = ttl };
        return this;
    }

    /// <summary>
    /// Sets the validity interval start slot.
    /// </summary>
    /// <param name="validityStart">The start slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetValidityIntervalStart(ulong validityStart)
    {
        Body = Body with { ValidityIntervalStart = validityStart };
        return this;
    }

    /// <summary>
    /// Adds a certificate to the transaction body.
    /// </summary>
    /// <param name="certificate">The certificate to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCertificate(Certificate certificate)
    {
        Body = Body with { Certificates = new CborDefListWithTag<Certificate>([.. Body.Certificates is not null ? Body.Certificates.GetValue() : [], certificate]) };
        return this;
    }

    /// <summary>
    /// Sets the withdrawals on the transaction body.
    /// </summary>
    /// <param name="withdrawals">The withdrawals to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        Body = Body with { Withdrawals = withdrawals };
        return this;
    }

    /// <summary>
    /// Sets the auxiliary data hash on the transaction body.
    /// </summary>
    /// <param name="hash">The hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        Body = Body with { AuxiliaryDataHash = hash };
        return this;
    }

    /// <summary>
    /// Adds a mint operation, merging with any existing mints.
    /// </summary>
    /// <param name="mint">The mint to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddMint(MultiAssetMint mint)
    {
        ArgumentNullException.ThrowIfNull(mint);

        if (Body.Mint == null)
        {
            Body = Body with { Mint = mint };
            return this;
        }
        MultiAssetMint mergedMint = MergeMints(Body.Mint, mint);

        Body = Body with { Mint = mergedMint };
        return this;
    }

    /// <summary>
    /// Replaces the entire mint operation.
    /// </summary>
    /// <param name="mint">The mint to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetMint(MultiAssetMint mint)
    {
        Body = Body with { Mint = mint };
        return this;
    }

    /// <summary>
    /// Sets the script data hash on the transaction body.
    /// </summary>
    /// <param name="hash">The script data hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetScriptDataHash(byte[] hash)
    {
        Body = Body with { ScriptDataHash = hash };
        return this;
    }

    /// <summary>
    /// Adds a collateral input.
    /// </summary>
    /// <param name="collateral">The collateral input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCollateral(TransactionInput collateral)
    {
        Body = Body with { Collateral = new CborDefListWithTag<TransactionInput>([.. Body.Collateral is not null ? Body.Collateral.GetValue() : [], collateral]) };
        return this;
    }

    /// <summary>
    /// Adds a required signer key hash.
    /// </summary>
    /// <param name="signer">The signer key hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddRequiredSigner(byte[] signer)
    {
        Body = Body with { RequiredSigners = new CborDefListWithTag<byte[]>([.. Body.RequiredSigners is not null ? Body.RequiredSigners.GetValue() : [], signer]) };
        return this;
    }

    /// <summary>
    /// Sets the collateral return output.
    /// </summary>
    /// <param name="collateralReturn">The collateral return output.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetCollateralReturn(TransactionOutput collateralReturn)
    {
        Body = Body with { CollateralReturn = collateralReturn };
        return this;
    }

    /// <summary>
    /// Sets the total collateral amount.
    /// </summary>
    /// <param name="totalCollateral">The total collateral in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTotalCollateral(ulong totalCollateral)
    {
        Body = Body with { TotalCollateral = totalCollateral };
        return this;
    }

    /// <summary>
    /// Adds a reference input.
    /// </summary>
    /// <param name="referenceInput">The reference input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        Body = Body with { ReferenceInputs = new CborDefListWithTag<TransactionInput>([.. Body.ReferenceInputs is not null ? Body.ReferenceInputs.GetValue() : [], referenceInput]) };
        return this;
    }

    /// <summary>
    /// Replaces all reference inputs.
    /// </summary>
    /// <param name="referenceInputs">The reference inputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetReferenceInputs(List<TransactionInput> referenceInputs)
    {
        Body = Body with { ReferenceInputs = new CborDefListWithTag<TransactionInput>(referenceInputs) };
        return this;
    }

    /// <summary>
    /// Sets the voting procedures on the transaction body.
    /// </summary>
    /// <param name="votingProcedures">The voting procedures to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        Body = Body with { VotingProcedures = votingProcedures };
        return this;
    }

    /// <summary>
    /// Adds a governance proposal procedure.
    /// </summary>
    /// <param name="proposal">The proposal to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        Body = Body with { ProposalProcedures = new CborDefListWithTag<ProposalProcedure>([.. Body.ProposalProcedures is not null ? Body.ProposalProcedures.GetValue() : [], proposal]) };
        return this;
    }

    /// <summary>
    /// Sets the treasury value.
    /// </summary>
    /// <param name="treasuryValue">The treasury value in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTreasuryValue(ulong treasuryValue)
    {
        Body = Body with { TreasuryValue = treasuryValue };
        return this;
    }

    /// <summary>
    /// Sets the donation amount.
    /// </summary>
    /// <param name="donation">The donation in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetDonation(ulong donation)
    {
        Body = Body with { Donation = donation };
        return this;
    }

    #endregion

    #region Witness Set Methods

    /// <summary>
    /// Adds a VKey witness to the witness set.
    /// </summary>
    /// <param name="witness">The VKey witness to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddVKeyWitness(VKeyWitness witness)
    {
        WitnessSet = WitnessSet with { VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>([.. WitnessSet.VKeyWitnessSet is not null ? WitnessSet.VKeyWitnessSet.GetValue() : [], witness]) };
        return this;
    }

    /// <summary>
    /// Adds a native script to the witness set.
    /// </summary>
    /// <param name="script">The native script to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddNativeScript(NativeScript script)
    {
        WitnessSet = WitnessSet with { NativeScriptSet = new CborDefListWithTag<NativeScript>([.. WitnessSet.NativeScriptSet is not null ? WitnessSet.NativeScriptSet.GetValue() : [], script]) };
        return this;
    }

    /// <summary>
    /// Adds a bootstrap witness to the witness set.
    /// </summary>
    /// <param name="witness">The bootstrap witness to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        WitnessSet = WitnessSet with { BootstrapWitnessSet = new CborDefListWithTag<BootstrapWitness>([.. WitnessSet.BootstrapWitnessSet is not null ? WitnessSet.BootstrapWitnessSet.GetValue() : [], witness]) };
        return this;
    }

    /// <summary>
    /// Adds a Plutus V1 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V1 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV1Script(byte[] script)
    {
        WitnessSet = WitnessSet with { PlutusV1ScriptSet = new CborDefListWithTag<byte[]>([.. WitnessSet.PlutusV1ScriptSet is not null ? WitnessSet.PlutusV1ScriptSet.GetValue() : [], script]) };
        return this;
    }

    /// <summary>
    /// Adds a Plutus V2 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V2 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV2Script(byte[] script)
    {
        WitnessSet = WitnessSet with { PlutusV2ScriptSet = new CborDefListWithTag<byte[]>([.. WitnessSet.PlutusV2ScriptSet is not null ? WitnessSet.PlutusV2ScriptSet.GetValue() : [], script]) };
        return this;
    }

    /// <summary>
    /// Adds a Plutus V3 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V3 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV3Script(byte[] script)
    {
        WitnessSet = WitnessSet with { PlutusV3ScriptSet = new CborDefListWithTag<byte[]>([.. WitnessSet.PlutusV3ScriptSet is not null ? WitnessSet.PlutusV3ScriptSet.GetValue() : [], script]) };
        return this;
    }

    /// <summary>
    /// Adds Plutus data to the witness set.
    /// </summary>
    /// <param name="data">The Plutus data to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusData(PlutusData data)
    {
        WitnessSet = WitnessSet with { PlutusDataSet = new CborDefListWithTag<PlutusData>([.. WitnessSet.PlutusDataSet is not null ? WitnessSet.PlutusDataSet.GetValue() : [], data]) };
        return this;
    }

    /// <summary>
    /// Sets the redeemers on the witness set.
    /// </summary>
    /// <param name="redeemers">The redeemers to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetRedeemers(Redeemers redeemers)
    {
        WitnessSet = WitnessSet with { Redeemers = redeemers };
        return this;
    }

    #endregion

    #region Auxiliary Data

    /// <summary>
    /// Sets the auxiliary data on the transaction.
    /// </summary>
    /// <param name="data">The auxiliary data to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetAuxiliaryData(PostAlonzoAuxiliaryDataMap data)
    {
        _auxiliaryData = data;
        return this;
    }

    /// <summary>
    /// Sets the metadata on the transaction auxiliary data.
    /// </summary>
    /// <param name="metadata">The metadata to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetMetadata(Metadata metadata)
    {
        _auxiliaryData = _auxiliaryData == null
            ? new PostAlonzoAuxiliaryDataMap(metadata, null, null, null, null)
            : _auxiliaryData with { MetadataValue = metadata };

        return this;
    }

    #endregion

    /// <summary>
    /// Builds and returns the complete transaction.
    /// </summary>
    /// <returns>The constructed PostMaryTransaction.</returns>
    public PostMaryTransaction Build()
    {
        return new PostMaryTransaction(Body, WitnessSet, true, _auxiliaryData);
    }

    private static MultiAssetMint MergeMints(MultiAssetMint existingMint, MultiAssetMint newMint)
    {
        // Use the custom comparer instead of default dictionary
        Dictionary<byte[], TokenBundleMint> result = new(ByteArrayEqualityComparer.Instance);

        // Copy existing mints
        foreach (KeyValuePair<byte[], TokenBundleMint> policyEntry in existingMint.Value)
        {
            result[policyEntry.Key] = policyEntry.Value;
        }

        // Merge new mints
        foreach (KeyValuePair<byte[], TokenBundleMint> policyEntry in newMint.Value)
        {
            if (result.TryGetValue(policyEntry.Key, out TokenBundleMint? existingTokenBundle))
            {
                // Merge token bundles using the same pattern
                Dictionary<byte[], long> mergedTokens = new(ByteArrayEqualityComparer.Instance);

                // Copy existing tokens
                foreach (KeyValuePair<byte[], long> token in existingTokenBundle.Value)
                {
                    mergedTokens[token.Key] = token.Value;
                }

                // Add new tokens
                foreach (KeyValuePair<byte[], long> tokenEntry in policyEntry.Value.Value)
                {
                    mergedTokens[tokenEntry.Key] = mergedTokens.TryGetValue(tokenEntry.Key, out long existingAmount)
                        ? existingAmount + tokenEntry.Value
                        : tokenEntry.Value;
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
