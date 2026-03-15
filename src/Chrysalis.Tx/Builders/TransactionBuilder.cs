using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Low-level fluent builder for constructing Cardano transactions.
/// Accumulates mutable state and constructs immutable CBOR types at Build() time.
/// </summary>
public class TransactionBuilder
{
    // ── Body ──

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

    // ── Witnesses ──

    private List<VKeyWitness>? _vkeyWitnesses;
    private List<INativeScript>? _nativeScripts;
    private List<BootstrapWitness>? _bootstrapWitnesses;
    private readonly List<(ReadOnlyMemory<byte> Bytes, int Version)> _plutusScripts = [];
    private List<IPlutusData>? _plutusData;

    // ── Auxiliary Data ──

    private PostAlonzoAuxiliaryDataMap? _auxiliaryData;

    // ── Properties ──

    /// <summary>Gets or sets the protocol parameters used for fee calculation.</summary>
    public ProtocolParams? Pparams { get; set; }

    /// <summary>Gets or sets the change output for fee adjustment.</summary>
    public ITransactionOutput? ChangeOutput { get; set; }

    /// <summary>Gets or sets the change output index for fee adjustment.</summary>
    public int? ChangeOutputIndex { get; set; }

    /// <summary>Gets the redeemer builder for deferred index computation.</summary>
    public RedeemerBuilder RedeemerSet { get; } = new();

    /// <summary>Gets the aggregate witness requirements.</summary>
    public WitnessRequirements WitnessReqs { get; } = new();

    // ── Read-Only Accessors ──

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

    /// <summary>Gets the current certificates.</summary>
    public ICborMaybeIndefList<ICertificate>? Certificates => WrapIfNotNull(_certificates);

    /// <summary>Gets the current collateral return output.</summary>
    public ITransactionOutput? CollateralReturn { get; private set; }

    /// <summary>Gets the current proposal procedures.</summary>
    public ICborMaybeIndefList<ProposalProcedure>? ProposalProcedures => WrapIfNotNull(_proposals);

    // ── Constructor ──

    /// <summary>Creates a new empty TransactionBuilder.</summary>
    public TransactionBuilder() { }

    /// <summary>Creates a new TransactionBuilder with protocol parameters.</summary>
    public static TransactionBuilder Create(ProtocolParams pparams) => new() { Pparams = pparams };

    // ── Inputs ──

    /// <summary>Adds a transaction input.</summary>
    public TransactionBuilder AddInput(TransactionInput input)
    {
        _inputs.Add(input);
        return this;
    }

    /// <summary>Adds a transaction input from hex hash and index.</summary>
    public TransactionBuilder AddInput(string txHashHex, ulong index)
    {
        _inputs.Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>Adds a transaction input from "txHash#index" format.</summary>
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
    public TransactionBuilder AddInput(InputBuilderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _inputs.Add(result.Input);
        WitnessReqs.Add(result.Requirements);
        _ = RedeemerSet.AddSpend(result);

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

    /// <summary>Replaces all transaction inputs.</summary>
    public TransactionBuilder SetInputs(List<TransactionInput> inputs)
    {
        _inputs = [.. inputs];
        return this;
    }

    // ── Outputs ──

    /// <summary>Adds a raw transaction output.</summary>
    public TransactionBuilder AddOutput(ITransactionOutput output, bool isChange = false) =>
        AddOutputInternal(output, isChange);

    /// <summary>Adds an output with a bech32 address and value.</summary>
    public TransactionBuilder AddOutput(string bech32Address, IValue amount, bool isChange = false) =>
        AddOutput(new OutputBuilder(bech32Address, amount) { }, isChange);

    /// <summary>Adds an output with raw address bytes and value.</summary>
    public TransactionBuilder AddOutput(byte[] addressBytes, IValue amount, bool isChange = false) =>
        AddOutput(new OutputBuilder(addressBytes, amount), isChange);

    /// <summary>Adds an output with an inline datum.</summary>
    public TransactionBuilder AddOutput<T>(string bech32Address, IValue amount, T datum, bool isChange = false) where T : ICborType =>
        AddOutput(new OutputBuilder(bech32Address, amount).WithInlineDatum(datum), isChange);

    /// <summary>Adds an output with an inline datum (byte[] address).</summary>
    public TransactionBuilder AddOutput<T>(byte[] addressBytes, IValue amount, T datum, bool isChange = false) where T : ICborType =>
        AddOutput(new OutputBuilder(addressBytes, amount).WithInlineDatum(datum), isChange);

    /// <summary>Adds an output with an inline datum and script reference.</summary>
    public TransactionBuilder AddOutput<T>(string bech32Address, IValue amount, T datum, IScript scriptRef, bool isChange = false) where T : ICborType =>
        AddOutput(new OutputBuilder(bech32Address, amount).WithInlineDatum(datum).WithScriptRef(scriptRef), isChange);

    /// <summary>Adds an output with an inline datum and script reference (byte[] address).</summary>
    public TransactionBuilder AddOutput<T>(byte[] addressBytes, IValue amount, T datum, IScript scriptRef, bool isChange = false) where T : ICborType =>
        AddOutput(new OutputBuilder(addressBytes, amount).WithInlineDatum(datum).WithScriptRef(scriptRef), isChange);

    /// <summary>Adds a fully configured output from an OutputBuilder.</summary>
    public TransactionBuilder AddOutput(OutputBuilder output, bool isChange = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (isChange || output.IsChange)
        {
            EnforceMinAda(output);
        }

        EnforceMinAda(output);
        return AddOutputInternal(output.Build(), isChange || output.IsChange);
    }

    private TransactionBuilder AddOutputInternal(ITransactionOutput output, bool isChange)
    {
        if (isChange)
        {
            ChangeOutput = output;
            ChangeOutputIndex = _outputs.Count;
        }

        _outputs.Add(output);
        return this;
    }

    private void EnforceMinAda(OutputBuilder output)
    {
        if (Pparams?.AdaPerUTxOByte is null)
        {
            return;
        }

        ulong adaPerByte = (ulong)Pparams.AdaPerUTxOByte;
        ITransactionOutput built = output.Build();
        ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerByte, CborSerializer.Serialize(built));
        ulong currentLovelace = built.Amount().Lovelace();

        while (currentLovelace < minLovelace)
        {
            IValue newAmount = output.Amount switch
            {
                LovelaceWithMultiAsset lma => LovelaceWithMultiAsset.Create(minLovelace, lma.MultiAsset),
                _ => Lovelace.Create(minLovelace)
            };
            output.Amount = newAmount;

            built = output.Build();
            minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerByte, CborSerializer.Serialize(built));
            currentLovelace = built.Amount().Lovelace();
        }
    }

    /// <summary>Replaces all transaction outputs.</summary>
    public TransactionBuilder SetOutputs(List<ITransactionOutput> outputs)
    {
        _outputs = [.. outputs];
        return this;
    }

    // ── Fee / TTL / Validity ──

    /// <summary>Sets the transaction fee.</summary>
    public TransactionBuilder SetFee(ulong feeAmount)
    {
        Fee = feeAmount;
        return this;
    }

    /// <summary>Sets the transaction time-to-live.</summary>
    public TransactionBuilder SetTtl(ulong ttl)
    {
        TimeToLive = ttl;
        return this;
    }

    /// <summary>Sets the validity interval start slot.</summary>
    public TransactionBuilder SetValidityIntervalStart(ulong validityStart)
    {
        _validityStart = validityStart;
        return this;
    }

    // ── Certificates / Withdrawals ──

    /// <summary>Adds a certificate.</summary>
    public TransactionBuilder AddCertificate(ICertificate certificate)
    {
        (_certificates ??= []).Add(certificate);
        return this;
    }

    /// <summary>Sets the withdrawals.</summary>
    public TransactionBuilder SetWithdrawals(Withdrawals withdrawals)
    {
        _withdrawals = withdrawals;
        return this;
    }

    // ── Minting ──

    /// <summary>Adds a mint operation, merging with any existing mints.</summary>
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

    /// <summary>Adds a single token mint/burn.</summary>
    public TransactionBuilder AddMint(string policyHex, string assetNameHex, long amount) =>
        AddMint(MintBuilder.Create().AddToken(policyHex, assetNameHex, amount).Build());

    /// <summary>Replaces the entire mint operation.</summary>
    public TransactionBuilder SetMint(MultiAssetMint mint)
    {
        Mint = mint;
        return this;
    }

    // ── Collateral ──

    /// <summary>Adds a collateral input.</summary>
    public TransactionBuilder AddCollateral(TransactionInput collateral)
    {
        (_collateral ??= []).Add(collateral);
        return this;
    }

    /// <summary>Adds a collateral input from hex hash and index.</summary>
    public TransactionBuilder AddCollateral(string txHashHex, ulong index)
    {
        (_collateral ??= []).Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>Adds a collateral input from "txHash#index" format.</summary>
    public TransactionBuilder AddCollateral(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);
        (string txHash, ulong index) = ParseUtxoRef(utxoRef);
        return AddCollateral(txHash, index);
    }

    /// <summary>Clears all collateral inputs.</summary>
    public TransactionBuilder ClearCollateral()
    {
        _collateral = null;
        return this;
    }

    /// <summary>Sets the collateral return output.</summary>
    public TransactionBuilder SetCollateralReturn(ITransactionOutput collateralReturn)
    {
        CollateralReturn = collateralReturn;
        return this;
    }

    /// <summary>Clears the collateral return output.</summary>
    public TransactionBuilder ClearCollateralReturn()
    {
        CollateralReturn = null;
        return this;
    }

    /// <summary>Sets the total collateral amount.</summary>
    public TransactionBuilder SetTotalCollateral(ulong totalCollateral)
    {
        TotalCollateral = totalCollateral;
        return this;
    }

    // ── Reference Inputs ──

    /// <summary>Adds a reference input.</summary>
    public TransactionBuilder AddReferenceInput(TransactionInput referenceInput)
    {
        (_referenceInputs ??= []).Add(referenceInput);
        return this;
    }

    /// <summary>Adds a reference input from hex hash and index.</summary>
    public TransactionBuilder AddReferenceInput(string txHashHex, ulong index)
    {
        (_referenceInputs ??= []).Add(TransactionInput.Create(Convert.FromHexString(txHashHex), index));
        return this;
    }

    /// <summary>Adds a reference input from "txHash#index" format.</summary>
    public TransactionBuilder AddReferenceInput(string utxoRef)
    {
        ArgumentNullException.ThrowIfNull(utxoRef);
        (string txHash, ulong index) = ParseUtxoRef(utxoRef);
        return AddReferenceInput(txHash, index);
    }

    /// <summary>Replaces all reference inputs.</summary>
    public TransactionBuilder SetReferenceInputs(List<TransactionInput> referenceInputs)
    {
        _referenceInputs = [.. referenceInputs];
        return this;
    }

    // ── Required Signers ──

    /// <summary>Adds a required signer key hash.</summary>
    public TransactionBuilder AddRequiredSigner(ReadOnlyMemory<byte> signer)
    {
        (_requiredSigners ??= []).Add(signer);
        return this;
    }

    /// <summary>Adds a required signer from a hex-encoded key hash.</summary>
    public TransactionBuilder AddRequiredSigner(string pkhHex)
    {
        (_requiredSigners ??= []).Add(Convert.FromHexString(pkhHex));
        return this;
    }

    // ── Governance ──

    /// <summary>Sets the voting procedures.</summary>
    public TransactionBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        _votingProcedures = votingProcedures;
        return this;
    }

    /// <summary>Adds a governance proposal procedure.</summary>
    public TransactionBuilder AddProposalProcedure(ProposalProcedure proposal)
    {
        (_proposals ??= []).Add(proposal);
        return this;
    }

    /// <summary>Sets the treasury value.</summary>
    public TransactionBuilder SetTreasuryValue(ulong treasuryValue)
    {
        _treasuryValue = treasuryValue;
        return this;
    }

    /// <summary>Sets the donation amount.</summary>
    public TransactionBuilder SetDonation(ulong donation)
    {
        _donation = donation;
        return this;
    }

    // ── Hashes / Auxiliary Data ──

    /// <summary>Sets the auxiliary data hash.</summary>
    public TransactionBuilder SetAuxiliaryDataHash(byte[] hash)
    {
        _auxDataHash = hash;
        return this;
    }

    /// <summary>Sets the script data hash.</summary>
    public TransactionBuilder SetScriptDataHash(byte[] hash)
    {
        _scriptDataHash = hash;
        return this;
    }

    /// <summary>Sets the network ID.</summary>
    public TransactionBuilder SetNetworkId(int networkId)
    {
        _networkId = networkId;
        return this;
    }

    // ── Witnesses ──

    /// <summary>Adds a VKey witness.</summary>
    public TransactionBuilder AddVKeyWitness(VKeyWitness witness)
    {
        (_vkeyWitnesses ??= []).Add(witness);
        return this;
    }

    /// <summary>Adds a native script.</summary>
    public TransactionBuilder AddNativeScript(INativeScript script)
    {
        (_nativeScripts ??= []).Add(script);
        return this;
    }

    /// <summary>Adds a bootstrap witness.</summary>
    public TransactionBuilder AddBootstrapWitness(BootstrapWitness witness)
    {
        (_bootstrapWitnesses ??= []).Add(witness);
        return this;
    }

    /// <summary>Adds a Plutus script, automatically routing by version.</summary>
    public TransactionBuilder AddPlutusScript(IScript script)
    {
        _plutusScripts.Add((script.Bytes(), script.Version()));
        return this;
    }

    /// <summary>Adds a Plutus V1 script.</summary>
    public TransactionBuilder AddPlutusV1Script(ReadOnlyMemory<byte> script)
    {
        _plutusScripts.Add((script, 1));
        return this;
    }

    /// <summary>Adds a Plutus V1 script from hex.</summary>
    public TransactionBuilder AddPlutusV1Script(string scriptHex)
    {
        _plutusScripts.Add((Convert.FromHexString(scriptHex), 1));
        return this;
    }

    /// <summary>Adds a Plutus V2 script.</summary>
    public TransactionBuilder AddPlutusV2Script(ReadOnlyMemory<byte> script)
    {
        _plutusScripts.Add((script, 2));
        return this;
    }

    /// <summary>Adds a Plutus V2 script from hex.</summary>
    public TransactionBuilder AddPlutusV2Script(string scriptHex)
    {
        _plutusScripts.Add((Convert.FromHexString(scriptHex), 2));
        return this;
    }

    /// <summary>Adds a Plutus V3 script.</summary>
    public TransactionBuilder AddPlutusV3Script(ReadOnlyMemory<byte> script)
    {
        _plutusScripts.Add((script, 3));
        return this;
    }

    /// <summary>Adds a Plutus V3 script from hex.</summary>
    public TransactionBuilder AddPlutusV3Script(string scriptHex)
    {
        _plutusScripts.Add((Convert.FromHexString(scriptHex), 3));
        return this;
    }

    /// <summary>Adds Plutus data to the witness set.</summary>
    public TransactionBuilder AddPlutusData(IPlutusData data)
    {
        (_plutusData ??= []).Add(data);
        return this;
    }

    /// <summary>Sets the redeemers.</summary>
    public TransactionBuilder SetRedeemers(IRedeemers redeemers)
    {
        Redeemers = redeemers;
        return this;
    }

    // ── Auxiliary Data ──

    /// <summary>Sets the auxiliary data.</summary>
    public TransactionBuilder SetAuxiliaryData(PostAlonzoAuxiliaryDataMap data)
    {
        _auxiliaryData = data;
        return this;
    }

    /// <summary>Sets the metadata.</summary>
    public TransactionBuilder SetMetadata(Metadata metadata)
    {
        _auxiliaryData = PostAlonzoAuxiliaryDataMap.Create(transactionMetadata: metadata);
        return this;
    }

    // ── Build ──

    /// <summary>Builds the transaction body.</summary>
    public ConwayTransactionBody BuildBody() => ConwayTransactionBody.Create(
        inputs: CborDefListWithTag<TransactionInput>.Create(_inputs),
        outputs: CborDefList<ITransactionOutput>.Create(_outputs),
        fee: Fee,
        timeToLive: TimeToLive,
        certificates: WrapIfNotNull(_certificates),
        withdrawals: _withdrawals,
        auxiliaryDataHash: _auxDataHash is not null ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(_auxDataHash) : null,
        validityIntervalStart: _validityStart,
        mint: Mint,
        scriptDataHash: _scriptDataHash is not null ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(_scriptDataHash) : null,
        collateral: WrapIfNotNull(_collateral),
        requiredSigners: WrapIfNotNull(_requiredSigners),
        networkId: _networkId,
        collateralReturn: CollateralReturn,
        totalCollateral: TotalCollateral,
        referenceInputs: WrapIfNotNull(_referenceInputs),
        votingProcedures: _votingProcedures,
        proposalProcedures: WrapIfNotNull(_proposals),
        treasuryValue: _treasuryValue,
        donation: _donation);

    /// <summary>Builds the witness set.</summary>
    public PostAlonzoTransactionWitnessSet BuildWitnessSet()
    {
        List<ReadOnlyMemory<byte>>? v1 = null, v2 = null, v3 = null;
        foreach ((ReadOnlyMemory<byte> bytes, int version) in _plutusScripts)
        {
            switch (version)
            {
                case 1: (v1 ??= []).Add(bytes); break;
                case 2: (v2 ??= []).Add(bytes); break;
                case 3: (v3 ??= []).Add(bytes); break;
                default: (v3 ??= []).Add(bytes); break;
            }
        }

        return PostAlonzoTransactionWitnessSet.Create(
            vKeyWitnesses: WrapIfNotNull(_vkeyWitnesses),
            nativeScripts: WrapIfNotNull(_nativeScripts),
            bootstrapWitnesses: WrapIfNotNull(_bootstrapWitnesses),
            plutusV1Scripts: WrapIfNotNull(v1),
            plutusDataSet: WrapIfNotNull(_plutusData),
            redeemers: Redeemers,
            plutusV2Scripts: WrapIfNotNull(v2),
            plutusV3Scripts: WrapIfNotNull(v3));
    }

    /// <summary>Builds and returns the complete transaction.</summary>
    public PostMaryTransaction Build() =>
        PostMaryTransaction.Create(BuildBody(), BuildWitnessSet(), true, _auxiliaryData);

    // ── Helpers ──

    private static CborDefListWithTag<T>? WrapIfNotNull<T>(List<T>? list) =>
        list is not null ? CborDefListWithTag<T>.Create(list) : null;

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
        Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> result = new(ReadOnlyMemoryComparer.Instance);

        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> entry in existingMint.Value)
        {
            result[entry.Key] = entry.Value;
        }

        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> entry in newMint.Value)
        {
            if (result.TryGetValue(entry.Key, out TokenBundleMint existing))
            {
                Dictionary<ReadOnlyMemory<byte>, long> merged = new(ReadOnlyMemoryComparer.Instance);
                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> t in existing.Value)
                {
                    merged[t.Key] = t.Value;
                }

                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> t in entry.Value.Value)
                {
                    merged[t.Key] = merged.TryGetValue(t.Key, out long amt) ? amt + t.Value : t.Value;
                }

                result[entry.Key] = TokenBundleMint.Create(merged);
            }
            else
            {
                result[entry.Key] = entry.Value;
            }
        }

        return MultiAssetMint.Create(result);
    }
}
