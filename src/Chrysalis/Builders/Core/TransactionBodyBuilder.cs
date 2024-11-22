using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Chrysalis.Builders.Core;

public class TransactionBodyBuilder : BuilderBase<TransactionBody>
{

    /// <summary>
    ///  Add an input to the transaction body. 
    /// </summary>
    /// <param name="input"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddInput(TransactionInput input)
    {
        // Notes: For all updating of fields we need a helper function.
        // For example, in this case, TransactionBody can have multiple types
        // first we need to determine what type it is (ie. ConwayTransactionBody) to access its values.
        // Then the type of the inputs field is a CborMaybeIndefList, which then has 
        // multiple types as well (ie. CborDefiniteList). Then and only then can we add the input.
        // It would be nice to have something like transactionBody.AddInput(input) helper that we can use
        // without having to worry about the underlying types.

        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple inputs to the transaction body.
    /// </summary>
    /// <param name="inputs"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on inputs see <see cref="TransactionInput"/>.</remarks>
    public void AddInputs(params TransactionInput[] inputs)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add an output to the transaction body.
    /// </summary>
    /// <param name="output"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on outputs see <see cref="TransactionOutput"/>.</remarks>
    public void AddOutput(TransactionOutput output)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple outputs to the transaction body.
    /// </summary>
    /// <param name="outputs"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on outputs see <see cref="TransactionOutput"/>.</remarks>
    public void AddOutputs(params TransactionOutput[] outputs)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the validity end time for the transaction.
    /// </summary>
    /// <param name="ttl"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetTimeToLive(ulong ttl)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attaches a certificate to the transaction body.
    /// </summary>
    /// <param name="certificate"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on certificates see <see cref="Certificate"/>.</remarks>
    public void AddCertificate(Certificate certificate)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attaches multiple certificates to the transaction body.
    /// </summary>
    /// <param name="certificates"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on certificates see <see cref="Certificate"/>.</remarks>
    public void AddCertificates(params Certificate[] certificates)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the withdrawals for the transaction body.
    /// </summary>
    /// <param name="withdrawals"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on withdrawals see <see cref="Withdrawals"/>.</remarks>
    public void SetWithdrawals(Withdrawals withdrawals)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the validity start time for the transaction.
    /// </summary>
    /// <param name="validityIntervalStart"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on validity start time see <see cref="TransactionBody.ValidityIntervalStart"/>.</remarks>
    public void SetValidityIntervalStart(ulong validityIntervalStart)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the mint for the transaction body.
    /// </summary>
    /// <param name="mint"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on mint see <see cref="MultiAssetMint"/>.</remarks>
    public void SetMint(MultiAssetMint mint)
    {
        // Notes: For sure we need a helper function here for multiasset mint. Unless
        // it's easy to construct a multiasset mint object. But in this context, we just
        // have to accept and set the mint object.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the script data hash for the transaction body.
    /// </summary>
    /// <param name="scriptDataHash"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <remarks>For more information on script data hash see <see cref="TransactionBody.ScriptDataHash"/>.</remarks>
    public void SetScriptDataHash(CborBytes scriptDataHash)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add collateral to the transaction body.
    /// </summary>
    /// <param name="input"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddCollateral(TransactionInput input)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple collaterals to the transaction body.
    /// </summary>
    /// <param name="inputs"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddCollateral(params TransactionInput[] inputs)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a required signer to the transaction body.
    /// </summary>
    /// <param name="requiredSigner"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddRequiredSigner(CborBytes requiredSigner)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple required signers to the transaction body.
    /// </summary>
    /// <param name="requiredSigners"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddRequiredSigners(params CborBytes[] requiredSigners)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the network id for the transaction body.
    /// </summary>
    /// <param name="networkId"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetNetworkId(CborInt networkId)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the collateral output for the transaction body.
    /// </summary>
    /// <param name="output"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetCollateralReturn(TransactionOutput output)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the total collateral for the transaction body.
    /// </summary>
    /// <param name="totalCollateral"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetTotalCollateral(ulong totalCollateral)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attaches a reference input to the transaction body.
    /// </summary>
    public void AddReferenceInput(TransactionInput referenceInput)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attaches multiple reference inputs to the transaction body.
    /// </summary>
    /// <param name="referenceInputs"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddReferenceInputs(params TransactionInput[] referenceInputs)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a voting procedure to the transaction body.
    /// </summary>
    /// <param name="votingProcedures"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddVotingProcedure(VotingProcedures<Voter, VoterChoices<GovActionId, VotingProcedure>> votingProcedures)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple voting procedures to the transaction body.
    /// </summary>
    /// <param name="votingProcedures"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddVotingProcedures(params VotingProcedures<Voter, VoterChoices<GovActionId, VotingProcedure>>[] votingProcedures)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add a proposal procedure to the transaction body.
    /// </summary>
    /// <param name="proposalProcedure"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddProposalProcedure(ProposalProcedure proposalProcedure)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Add multiple proposal procedures to the transaction body.
    /// </summary>
    /// <param name="proposalProcedures"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddProposalProcedures(params ProposalProcedure[] proposalProcedures)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the treasury value for the transaction body.
    /// </summary>
    /// <param name="treasuryValue"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetTreasuryValue(ulong treasuryValue)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Set the donation for the transaction body.
    /// </summary>
    /// <param name="donation"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void SetDonation(ulong donation)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Build the transaction body.
    /// </summary>
    /// <returns>The built transaction body</returns>
    public override TransactionBody Build()
    {
        throw new NotImplementedException();
    }
}