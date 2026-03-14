using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Extensions;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Fluent builder for constructing Cardano transactions.
/// Stores mutable internal state and constructs V2 readonly record structs only at Build() time.
/// </summary>
public class TransactionBuilder
{
    // ──────────── Transaction Body State ────────────

    private List<TransactionInput> _inputs = [];
    private List<ITransactionOutput> _outputs = [];
    private ulong? _validityStart;
    private List<ICertificate>? _certificates;
    private Withdrawals? _withdrawals;
    private byte[]? _auxDataHash;
    private byte[]? _scriptDataHash;
    private List<TransactionInput>? _collateral;
    private List<ReadOnlyMemory<byte>>? _requiredSigners;
    private List<TransactionInput>? _referenceInputs;
    private VotingProcedures? _votingProcedures;
    private List<ProposalProcedure>? _proposals;
    private ulong? _treasuryValue;
    private ulong? _donation;
    private int? _networkId;

    // ──────────── Witness Set State ────────────

    private List<VKeyWitness>? _vkeyWitnesses;
    private List<INativeScript>? _nativeScripts;
    private List<BootstrapWitness>? _bootstrapWitnesses;
    private List<ReadOnlyMemory<byte>>? _plutusV1Scripts;
    private List<ReadOnlyMemory<byte>>? _plutusV2Scripts;
    private List<ReadOnlyMemory<byte>>? _plutusV3Scripts;
    private List<IPlutusData>? _plutusData;

    // ──────────── Auxiliary Data ────────────

    private PostAlonzoAuxiliaryDataMap? _auxiliaryData;

    // ──────────── Builder Integration ────────────

    // ──────────── Other Properties ────────────

    /// <summary>Gets or sets the protocol parameters used for fee calculation.</summary>
    public ProtocolParams? Pparams { get; set; }

    /// <summary>Gets or sets the change output for fee adjustment.</summary>
    public ITransactionOutput? ChangeOutput { get; set; }

    /// <summary>Gets the redeemer builder for deferred index computation.</summary>
    public RedeemerBuilder RedeemerSet { get; } = new();

    /// <summary>Gets the aggregate witness requirements.</summary>
    public WitnessRequirements WitnessReqs { get; } = new();

    /// <summary>Gets or sets the change output index for fee adjustment.</summary>
    public int? ChangeOutputIndex { get; set; }

    // ──────────── Read-Only Accessors ────────────

    /// <summary>Gets the current transaction inputs.</summary>
    public IReadOnlyList<TransactionInput> Inputs => _inputs;

    /// <summary>Gets the current transaction outputs.</summary>
    public IReadOnlyList<ITransactionOutput> Outputs => _outputs;

    /// <summary>Gets the current fee.</summary>
    public ulong Fee { get; private set; }

    /// <summary>Gets the current time-to-live.</summary>
    public ulong? TimeToLive { get; private set; }

    /// <summary>Gets the current total collateral.</summary>
    public ulong? TotalCollateral { get; private set; }

    /// <summary>Gets the current mint.</summary>
    public MultiAssetMint? Mint { get; private set; }

    /// <summary>Gets the current collateral inputs.</summary>
    public IReadOnlyList<TransactionInput>? Collateral => _collateral;

    /// <summary>Gets the current reference inputs.</summary>
    public IReadOnlyList<TransactionInput>? ReferenceInputs => _referenceInputs;

    /// <summary>Gets the current required signers.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>>? RequiredSigners => _requiredSigners;

    /// <summary>Gets the current redeemers.</summary>
    public IRedeemers? Redeemers { get; private set; }

    /// <summary>Gets the current Plutus data set.</summary>
    public IReadOnlyList<IPlutusData>? PlutusDataSet => _plutusData;

    /// <summary>Gets the current certificates as a CBOR list.</summary>
    public ICborMaybeIndefList<ICertificate>? Certificates => WrapIfNotNull(_certificates);

    /// <summary>Gets the current collateral return output.</summary>
    public ITransactionOutput? CollateralReturn { get; private set; }

    /// <summary>Gets the current proposal procedures as a CBOR list.</summary>
    public ICborMaybeIndefList<ProposalProcedure>? ProposalProcedures => WrapIfNotNull(_proposals);

    /// <summary>
    /// Initializes a new TransactionBuilder with empty state.
    /// </summary>
    public TransactionBuilder() => _auxiliaryData = null;

    /// <summary>
    /// Creates a new TransactionBuilder with the given protocol parameters.
    /// </summary>
    /// <param name="pparams">The protocol parameters.</param>
    /// <returns>A new TransactionBuilder instance.</returns>
    public static TransactionBuilder Create(ProtocolParams pparams) => new() { Pparams = pparams };

    #region Transaction Body Methods

    /// <summary>
    /// Sets the network ID on the transaction body.
    /// </summary>
    /// <param name="networkId">The network ID (0 for testnet, 1 for mainnet).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetNetworkId(int networkId)
    {
        _networkId = networkId;
        return this;
    }

    /// <summary>
    /// Adds a transaction input to the body.
    /// </summary>
    /// <param name="input">The transaction input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddInput(TransactionInput input)
    {
        _inputs.Add(input);
        return this;
    }

    /// <summary>
    /// Adds a transaction input from a hex-encoded transaction hash and index.
    /// </summary>
    /// <param name="txHashHex">The hex-encoded transaction hash (64 characters).</param>
    /// <param name="index">The output index.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddInput(string txHashHex, ulong index)
    {
        _inputs.Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>
    /// Adds a transaction input from a UTxO reference string in <c>"txHash#index"</c> format.
    /// </summary>
    /// <param name="utxoRef">The UTxO reference (e.g., <c>"abcd...1234#0"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddInput(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);
        (string txHash, ulong index) = ParseUtxoRef(utxoRef);
        return AddInput(txHash, index);
    }

    /// <summary>
    /// Adds an input built with <see cref="InputBuilder"/>, automatically tracking
    /// witness requirements, scripts, datums, and redeemers.
    /// </summary>
    /// <param name="result">The input builder result with witness requirements.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddInput(InputBuilderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _inputs.Add(result.Input);
        WitnessReqs.Add(result.Requirements);
        RedeemerSet.AddSpend(result);

        foreach (IScript script in result.Requirements.ScriptWitnesses)
        {
            _ = AddPlutusScript(script);
        }

        foreach (IPlutusData datum in result.Requirements.Datums)
        {
            _ = AddPlutusData(datum);
        }

        foreach (string signer in result.Requirements.RequiredSigners)
        {
            _ = AddRequiredSigner(Convert.FromHexString(signer));
        }

        return this;
    }

    /// <summary>
    /// Replaces all transaction inputs.
    /// </summary>
    /// <param name="inputs">The inputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetInputs(List<TransactionInput> inputs)
    {
        _inputs = [.. inputs];
        return this;
    }

    /// <summary>
    /// Adds a transaction output, optionally marking it as the change output.
    /// </summary>
    /// <param name="output">The transaction output to add.</param>
    /// <param name="isChange">Whether this is the change output for fee adjustment.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddOutput(ITransactionOutput output, bool isChange = false)
    {
        if (isChange)
        {
            ChangeOutput = output;
        }

        _outputs.Add(output);

        return this;
    }

    /// <summary>
    /// Starts building a transaction output with the given bech32 address and value.
    /// Call terminal methods on the returned <see cref="OutputBuilder"/> to configure and add the output.
    /// </summary>
    /// <param name="bech32Address">The bech32-encoded destination address.</param>
    /// <param name="amount">The output value.</param>
    /// <returns>An <see cref="OutputBuilder"/> for fluent configuration.</returns>
    public OutputBuilder AddOutput(string bech32Address, IValue amount)
    {
        byte[] addressBytes = Wallet.Models.Addresses.Address.FromBech32(bech32Address).ToBytes();
        return new OutputBuilder(this, addressBytes, amount);
    }

    /// <summary>
    /// Starts building a transaction output with raw address bytes and value.
    /// Call terminal methods on the returned <see cref="OutputBuilder"/> to configure and add the output.
    /// </summary>
    /// <param name="addressBytes">The raw address bytes.</param>
    /// <param name="amount">The output value.</param>
    /// <returns>An <see cref="OutputBuilder"/> for fluent configuration.</returns>
    public OutputBuilder AddOutput(byte[] addressBytes, IValue amount) =>
        new(this, addressBytes, amount);

    /// <summary>
    /// Replaces all transaction outputs.
    /// </summary>
    /// <param name="outputs">The outputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetOutputs(List<ITransactionOutput> outputs)
    {
        _outputs = [.. outputs];
        return this;
    }

    /// <summary>
    /// Sets the transaction fee.
    /// </summary>
    /// <param name="feeAmount">The fee in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetFee(ulong feeAmount)
    {
        Fee = feeAmount;
        return this;
    }

    /// <summary>
    /// Sets the transaction time-to-live (TTL).
    /// </summary>
    /// <param name="ttl">The TTL slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTtl(ulong ttl)
    {
        TimeToLive = ttl;
        return this;
    }

    /// <summary>
    /// Sets the validity interval start slot.
    /// </summary>
    /// <param name="validityStart">The start slot number.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetValidityIntervalStart(ulong validityStart)
    {
        _validityStart = validityStart;
        return this;
    }

    /// <summary>
    /// Adds a certificate to the transaction body.
    /// </summary>
    /// <param name="certificate">The certificate to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCertificate(ICertificate certificate)
    {
        (_certificates ??= []).Add(certificate);
        return this;
    }

    /// <summary>
    /// Sets the withdrawals on the transaction body.
    /// </summary>
    /// <param name="withdrawals">The withdrawals to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        _withdrawals = withdrawals;
        return this;
    }

    /// <summary>
    /// Sets the auxiliary data hash on the transaction body.
    /// </summary>
    /// <param name="hash">The hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        _auxDataHash = hash;
        return this;
    }

    /// <summary>
    /// Adds a mint operation, merging with any existing mints.
    /// </summary>
    /// <param name="mint">The mint to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddMint(MultiAssetMint mint)
    {
        if (Mint == null)
        {
            Mint = mint;
            return this;
        }

        Mint = MergeMints(Mint.Value, mint);
        return this;
    }

    /// <summary>
    /// Adds a single token mint/burn, merging with any existing mints.
    /// </summary>
    /// <param name="policyHex">The hex-encoded policy ID (56 characters).</param>
    /// <param name="assetNameHex">The hex-encoded asset name.</param>
    /// <param name="amount">The quantity to mint (positive) or burn (negative).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddMint(string policyHex, string assetNameHex, long amount) =>
        AddMint(MintBuilder.Create().AddToken(policyHex, assetNameHex, amount).Build());

    /// <summary>
    /// Replaces the entire mint operation.
    /// </summary>
    /// <param name="mint">The mint to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetMint(MultiAssetMint mint)
    {
        Mint = mint;
        return this;
    }

    /// <summary>
    /// Sets the script data hash on the transaction body.
    /// </summary>
    /// <param name="hash">The script data hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetScriptDataHash(byte[] hash)
    {
        _scriptDataHash = hash;
        return this;
    }

    /// <summary>
    /// Adds a collateral input.
    /// </summary>
    /// <param name="collateral">The collateral input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCollateral(TransactionInput collateral)
    {
        (_collateral ??= []).Add(collateral);
        return this;
    }

    /// <summary>
    /// Adds a collateral input from a hex-encoded transaction hash and index.
    /// </summary>
    /// <param name="txHashHex">The hex-encoded transaction hash (64 characters).</param>
    /// <param name="index">The output index.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCollateral(string txHashHex, ulong index)
    {
        (_collateral ??= []).Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>
    /// Adds a collateral input from a UTxO reference string in <c>"txHash#index"</c> format.
    /// </summary>
    /// <param name="utxoRef">The UTxO reference (e.g., <c>"abcd...1234#0"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddCollateral(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);
        (string txHash, ulong index) = ParseUtxoRef(utxoRef);
        return AddCollateral(txHash, index);
    }

    /// <summary>
    /// Clears all collateral inputs.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder ClearCollateral()
    {
        _collateral = null;
        return this;
    }

    /// <summary>
    /// Adds a required signer key hash.
    /// </summary>
    /// <param name="signer">The signer key hash bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddRequiredSigner(ReadOnlyMemory<byte> signer)
    {
        (_requiredSigners ??= []).Add(signer);
        return this;
    }

    /// <summary>
    /// Adds a required signer from a hex-encoded public key hash.
    /// </summary>
    /// <param name="pkhHex">The hex-encoded public key hash (56 characters).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddRequiredSigner(string pkhHex)
    {
        (_requiredSigners ??= []).Add(Convert.FromHexString(pkhHex));
        return this;
    }

    /// <summary>
    /// Sets the collateral return output.
    /// </summary>
    /// <param name="collateralReturn">The collateral return output.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetCollateralReturn(ITransactionOutput collateralReturn)
    {
        CollateralReturn = collateralReturn;
        return this;
    }

    /// <summary>
    /// Clears the collateral return output.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder ClearCollateralReturn()
    {
        CollateralReturn = null;
        return this;
    }

    /// <summary>
    /// Sets the total collateral amount.
    /// </summary>
    /// <param name="totalCollateral">The total collateral in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTotalCollateral(ulong totalCollateral)
    {
        TotalCollateral = totalCollateral;
        return this;
    }

    /// <summary>
    /// Adds a reference input.
    /// </summary>
    /// <param name="referenceInput">The reference input to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        (_referenceInputs ??= []).Add(referenceInput);
        return this;
    }

    /// <summary>
    /// Adds a reference input from a hex-encoded transaction hash and index.
    /// </summary>
    /// <param name="txHashHex">The hex-encoded transaction hash (64 characters).</param>
    /// <param name="index">The output index.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddReferenceInput(string txHashHex, ulong index)
    {
        (_referenceInputs ??= []).Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>
    /// Adds a reference input from a UTxO reference string in <c>"txHash#index"</c> format.
    /// </summary>
    /// <param name="utxoRef">The UTxO reference (e.g., <c>"abcd...1234#0"</c>).</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddReferenceInput(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);
        (string txHash, ulong index) = ParseUtxoRef(utxoRef);
        return AddReferenceInput(txHash, index);
    }

    /// <summary>
    /// Replaces all reference inputs.
    /// </summary>
    /// <param name="referenceInputs">The reference inputs to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetReferenceInputs(List<TransactionInput> referenceInputs)
    {
        _referenceInputs = [.. referenceInputs];
        return this;
    }

    /// <summary>
    /// Sets the voting procedures on the transaction body.
    /// </summary>
    /// <param name="votingProcedures">The voting procedures to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        _votingProcedures = votingProcedures;
        return this;
    }

    /// <summary>
    /// Adds a governance proposal procedure.
    /// </summary>
    /// <param name="proposal">The proposal to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        (_proposals ??= []).Add(proposal);
        return this;
    }

    /// <summary>
    /// Sets the treasury value.
    /// </summary>
    /// <param name="treasuryValue">The treasury value in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetTreasuryValue(ulong treasuryValue)
    {
        _treasuryValue = treasuryValue;
        return this;
    }

    /// <summary>
    /// Sets the donation amount.
    /// </summary>
    /// <param name="donation">The donation in lovelace.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetDonation(ulong donation)
    {
        _donation = donation;
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
        (_vkeyWitnesses ??= []).Add(witness);
        return this;
    }

    /// <summary>
    /// Adds a native script to the witness set.
    /// </summary>
    /// <param name="script">The native script to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddNativeScript(INativeScript script)
    {
        (_nativeScripts ??= []).Add(script);
        return this;
    }

    /// <summary>
    /// Adds a bootstrap witness to the witness set.
    /// </summary>
    /// <param name="witness">The bootstrap witness to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        (_bootstrapWitnesses ??= []).Add(witness);
        return this;
    }

    /// <summary>
    /// Adds a Plutus script to the witness set, automatically routing by version.
    /// </summary>
    /// <param name="script">The Plutus script.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusScript(IScript script)
    {
        ReadOnlyMemory<byte> bytes = script.Bytes();
        return script.Version() switch
        {
            1 => AddPlutusV1Script(bytes),
            2 => AddPlutusV2Script(bytes),
            3 => AddPlutusV3Script(bytes),
            _ => AddPlutusV3Script(bytes),
        };
    }

    /// <summary>
    /// Adds a Plutus V1 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V1 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV1Script(ReadOnlyMemory<byte> script)
    {
        (_plutusV1Scripts ??= []).Add(script);
        return this;
    }

    /// <summary>
    /// Adds a Plutus V1 script from a hex-encoded string.
    /// </summary>
    /// <param name="scriptHex">The hex-encoded Plutus V1 script.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV1Script(string scriptHex)
    {
        (_plutusV1Scripts ??= []).Add(Convert.FromHexString(scriptHex));
        return this;
    }

    /// <summary>
    /// Adds a Plutus V2 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V2 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV2Script(ReadOnlyMemory<byte> script)
    {
        (_plutusV2Scripts ??= []).Add(script);
        return this;
    }

    /// <summary>
    /// Adds a Plutus V2 script from a hex-encoded string.
    /// </summary>
    /// <param name="scriptHex">The hex-encoded Plutus V2 script.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV2Script(string scriptHex)
    {
        (_plutusV2Scripts ??= []).Add(Convert.FromHexString(scriptHex));
        return this;
    }

    /// <summary>
    /// Adds a Plutus V3 script to the witness set.
    /// </summary>
    /// <param name="script">The Plutus V3 script bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV3Script(ReadOnlyMemory<byte> script)
    {
        (_plutusV3Scripts ??= []).Add(script);
        return this;
    }

    /// <summary>
    /// Adds a Plutus V3 script from a hex-encoded string.
    /// </summary>
    /// <param name="scriptHex">The hex-encoded Plutus V3 script.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusV3Script(string scriptHex)
    {
        (_plutusV3Scripts ??= []).Add(Convert.FromHexString(scriptHex));
        return this;
    }

    /// <summary>
    /// Adds Plutus data to the witness set.
    /// </summary>
    /// <param name="data">The Plutus data to add.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder AddPlutusData(IPlutusData data)
    {
        (_plutusData ??= []).Add(data);
        return this;
    }

    /// <summary>
    /// Sets the redeemers on the witness set.
    /// </summary>
    /// <param name="redeemers">The redeemers to set.</param>
    /// <returns>This builder for chaining.</returns>
    public TransactionBuilder SetRedeemers(IRedeemers redeemers)
    {
        Redeemers = redeemers;
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
        _auxiliaryData = PostAlonzoAuxiliaryDataMap.Create(transactionMetadata: metadata);
        return this;
    }

    #endregion

    /// <summary>
    /// Builds the ConwayTransactionBody from the current state.
    /// </summary>
    /// <returns>The constructed ConwayTransactionBody.</returns>
    public ConwayTransactionBody BuildBody() => ConwayTransactionBody.Create(
            inputs: CborDefListWithTag<TransactionInput>.Create(_inputs),
            outputs: CborDefList<ITransactionOutput>.Create(_outputs),
            fee: Fee,
            timeToLive: TimeToLive,
            certificates: WrapIfNotNull(_certificates),
            withdrawals: _withdrawals,
            auxiliaryDataHash: _auxDataHash != null ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(_auxDataHash) : null,
            validityIntervalStart: _validityStart,
            mint: Mint,
            scriptDataHash: _scriptDataHash != null ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(_scriptDataHash) : null,
            collateral: WrapIfNotNull(_collateral),
            requiredSigners: WrapIfNotNull(_requiredSigners),
            networkId: _networkId,
            collateralReturn: CollateralReturn,
            totalCollateral: TotalCollateral,
            referenceInputs: WrapIfNotNull(_referenceInputs),
            votingProcedures: _votingProcedures,
            proposalProcedures: WrapIfNotNull(_proposals),
            treasuryValue: _treasuryValue,
            donation: _donation
        );

    /// <summary>
    /// Builds the PostAlonzoTransactionWitnessSet from the current state.
    /// </summary>
    /// <returns>The constructed PostAlonzoTransactionWitnessSet.</returns>
    public PostAlonzoTransactionWitnessSet BuildWitnessSet() => PostAlonzoTransactionWitnessSet.Create(
            vKeyWitnesses: WrapIfNotNull(_vkeyWitnesses),
            nativeScripts: WrapIfNotNull(_nativeScripts),
            bootstrapWitnesses: WrapIfNotNull(_bootstrapWitnesses),
            plutusV1Scripts: WrapIfNotNull(_plutusV1Scripts),
            plutusDataSet: WrapIfNotNull(_plutusData),
            redeemers: Redeemers,
            plutusV2Scripts: WrapIfNotNull(_plutusV2Scripts),
            plutusV3Scripts: WrapIfNotNull(_plutusV3Scripts)
        );

    /// <summary>
    /// Builds and returns the complete transaction.
    /// </summary>
    /// <returns>The constructed PostMaryTransaction.</returns>
    public PostMaryTransaction Build()
    {
        ConwayTransactionBody body = BuildBody();
        PostAlonzoTransactionWitnessSet witnessSet = BuildWitnessSet();

        return PostMaryTransaction.Create(body, witnessSet, true, _auxiliaryData);
    }

    private static CborDefListWithTag<T>? WrapIfNotNull<T>(List<T>? list)
        => list != null ? CborDefListWithTag<T>.Create(list) : null;

    /// <summary>
    /// Parses a UTxO reference in <c>"txHash#index"</c> format into its components.
    /// </summary>
    private static (string TxHash, ulong Index) ParseUtxoRef(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);

        int hashIndex = utxoRef.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex < 0)
        {
            throw new FormatException($"Invalid UTxO reference '{utxoRef}'. Expected format: txHash#index");
        }

        return (utxoRef[..hashIndex], ulong.Parse(utxoRef[(hashIndex + 1)..], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static MultiAssetMint MergeMints(MultiAssetMint existingMint, MultiAssetMint newMint)
    {
        // Use the custom comparer instead of default dictionary
        Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> result = new(ReadOnlyMemoryComparer.Instance);

        // Copy existing mints
        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> policyEntry in existingMint.Value)
        {
            result[policyEntry.Key] = policyEntry.Value;
        }

        // Merge new mints
        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> policyEntry in newMint.Value)
        {
            if (result.TryGetValue(policyEntry.Key, out TokenBundleMint existingTokenBundle))
            {
                // Merge token bundles using the same pattern
                Dictionary<ReadOnlyMemory<byte>, long> mergedTokens = new(ReadOnlyMemoryComparer.Instance);

                // Copy existing tokens
                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> token in existingTokenBundle.Value)
                {
                    mergedTokens[token.Key] = token.Value;
                }

                // Add new tokens
                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> tokenEntry in policyEntry.Value.Value)
                {
                    mergedTokens[tokenEntry.Key] = mergedTokens.TryGetValue(tokenEntry.Key, out long existingAmount)
                        ? existingAmount + tokenEntry.Value
                        : tokenEntry.Value;
                }

                result[policyEntry.Key] = TokenBundleMint.Create(mergedTokens);
            }
            else
            {
                result[policyEntry.Key] = policyEntry.Value;
            }
        }

        return MultiAssetMint.Create(result);
    }

}
