using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Mid-level fluent transaction builder with automatic balancing.
/// Sits between the low-level <see cref="TransactionBuilder"/> (manual everything)
/// and the high-level <see cref="TransactionTemplateBuilder{T}"/> (parametric templates).
/// Call <see cref="Complete"/> to auto-balance with coin selection, fee calculation,
/// change output, and collateral handling.
/// </summary>
public class TxBuilder
{
    private readonly ICardanoDataProvider _provider;
    private ProtocolParams? _pparams;

    // ── Explicit inputs (always included) ──
    private readonly List<InputDirective> _explicitInputs = [];

    // ── UTxO pool for coin selection (only spent if deficit) ──
    private readonly List<ResolvedInput> _unspentOutputs = [];

    // ── Reference inputs ──
    private readonly List<ResolvedInput> _referenceInputs = [];

    // ── Output directives ──
    private readonly List<OutputDirective> _outputDirectives = [];

    // ── Mint directives ──
    private readonly List<MintDirective> _mintDirectives = [];

    // ── Certificates ──
    private readonly List<CertificateDirective> _certificateDirectives = [];

    // ── Withdrawals ──
    private readonly List<WithdrawalDirective> _withdrawalDirectives = [];

    // ── Scripts & Datums (explicit) ──
    private readonly List<IScript> _providedScripts = [];
    private readonly List<IPlutusData> _providedDatums = [];

    // ── Collateral (optional, auto-selected if empty) ──
    private List<ResolvedInput>? _collateralPool;

    // ── Configuration ──
    private string? _changeAddress;
    private ulong? _validFrom;
    private ulong? _validUntil;
    private int? _networkId;
    private readonly List<string> _requiredSigners = [];
    private readonly Dictionary<ulong, ITransactionMetadatum> _metadata = [];
    private CoinSelectionStrategy _coinSelectionStrategy = CoinSelectionStrategy.LargestFirst;

    // ── Governance ──
    private VotingProcedures? _votingProcedures;
    private readonly List<ProposalProcedure> _proposals = [];
    private ulong? _treasuryValue;
    private ulong? _donation;

    // ── Fee tuning ──
    private ulong _feePadding;
    private ulong? _minimumFee;

    // ── External evaluator ──
    private Func<PostMaryTransaction, IReadOnlyList<ResolvedInput>, Task<IReadOnlyList<Plutus.VM.Models.EvaluationResult>>>? _evaluator;

    // ── Constructor ──

    /// <summary>Creates a new mid-level transaction builder with a data provider.</summary>
    public TxBuilder(ICardanoDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    // ── Inputs ──

    /// <summary>Explicitly spend a UTxO (key-based, no script).</summary>
    public TxBuilder AddInput(ResolvedInput utxo)
    {
        _explicitInputs.Add(new InputDirective(utxo, null, null));
        return this;
    }

    /// <summary>Explicitly spend a script UTxO with redeemer. Auto-detects script from UTxO output or reference inputs.</summary>
    public TxBuilder AddInput(ResolvedInput utxo, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        _explicitInputs.Add(new InputDirective(utxo, ToPlutusData(redeemer), null));
        return this;
    }

    /// <summary>Explicitly spend a script UTxO with redeemer and explicit datum (for datum-hash UTxOs).</summary>
    public TxBuilder AddInput(ResolvedInput utxo, ICborType redeemer, ICborType datum)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        ArgumentNullException.ThrowIfNull(datum);
        _explicitInputs.Add(new InputDirective(utxo, ToPlutusData(redeemer), ToPlutusData(datum)));
        return this;
    }

    /// <summary>Provide UTxO pool for coin selection (not spent unless needed to cover deficit).</summary>
    public TxBuilder AddUnspentOutputs(IEnumerable<ResolvedInput> utxos)
    {
        ArgumentNullException.ThrowIfNull(utxos);
        _unspentOutputs.AddRange(utxos);
        return this;
    }

    /// <summary>Add a non-spending reference input (for script validation).</summary>
    public TxBuilder AddReferenceInput(ResolvedInput utxo)
    {
        _referenceInputs.Add(utxo);
        return this;
    }

    // ── Outputs ──

    /// <summary>Pay ADA to an address.</summary>
    public TxBuilder PayLovelace(string bech32Address, ulong lovelace)
    {
        _outputDirectives.Add(new OutputDirective(bech32Address, Lovelace.Create(lovelace)));
        return this;
    }

    /// <summary>Pay value (ADA + tokens) to an address.</summary>
    public TxBuilder PayAssets(string bech32Address, IValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _outputDirectives.Add(new OutputDirective(bech32Address, value));
        return this;
    }

    /// <summary>Lock ADA at a script address with an inline datum.</summary>
    public TxBuilder LockLovelace<T>(string bech32Address, ulong lovelace, T datum) where T : ICborType
    {
        _outputDirectives.Add(new OutputDirective(bech32Address, Lovelace.Create(lovelace), CborSerializer.Serialize(datum)));
        return this;
    }

    /// <summary>Lock value at a script address with an inline datum.</summary>
    public TxBuilder LockAssets<T>(string bech32Address, IValue value, T datum) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(value);
        _outputDirectives.Add(new OutputDirective(bech32Address, value, CborSerializer.Serialize(datum)));
        return this;
    }

    /// <summary>Lock value at a script address with an inline datum and script reference.</summary>
    public TxBuilder LockAssets<T>(string bech32Address, IValue value, T datum, IScript scriptRef) where T : ICborType
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(scriptRef);
        _outputDirectives.Add(new OutputDirective(bech32Address, value, CborSerializer.Serialize(datum), scriptRef));
        return this;
    }

    /// <summary>Add a raw transaction output (escape hatch).</summary>
    public TxBuilder AddOutput(ITransactionOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputDirectives.Add(new OutputDirective(output));
        return this;
    }

    /// <summary>Deploy a script as a reference UTxO at a burn address.</summary>
    public TxBuilder DeployScript(IScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        byte[] burnAddr = [0x61, .. new byte[28]];
        _outputDirectives.Add(new OutputDirective(burnAddr, Lovelace.Create(0), datumCbor: null, script));
        return this;
    }

    /// <summary>Deploy a script as a reference UTxO at a specified address.</summary>
    public TxBuilder DeployScript(IScript script, string bech32Address)
    {
        ArgumentNullException.ThrowIfNull(script);
        _outputDirectives.Add(new OutputDirective(bech32Address, Lovelace.Create(0), datumCbor: null, script));
        return this;
    }

    // ── Minting ──

    /// <summary>Mint/burn assets with a Plutus script.</summary>
    public TxBuilder AddMint(string policyHex, Dictionary<string, long> assets, IScript script, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(redeemer);
        _mintDirectives.Add(new MintDirective(policyHex, assets, script, null, ToPlutusData(redeemer)));
        return this;
    }

    /// <summary>Mint/burn assets with a native script.</summary>
    public TxBuilder AddMint(string policyHex, Dictionary<string, long> assets, INativeScript script)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(script);
        _mintDirectives.Add(new MintDirective(policyHex, assets, null, script, null));
        return this;
    }

    /// <summary>Mint/burn assets with a reference script.</summary>
    public TxBuilder AddMint(string policyHex, Dictionary<string, long> assets, string scriptHash, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(redeemer);
        _mintDirectives.Add(new MintDirective(policyHex, assets, null, null, ToPlutusData(redeemer)) { ScriptRefHash = scriptHash });
        return this;
    }

    // ── Certificates & Staking ──

    /// <summary>Delegate stake credential to a pool.</summary>
    public TxBuilder AddDelegation(Credential delegator, string poolIdHex)
    {
        ICertificate cert = StakeDelegation.Create(2, delegator, Convert.FromHexString(poolIdHex));
        _certificateDirectives.Add(new CertificateDirective(cert));
        return this;
    }

    /// <summary>Delegate stake credential to a pool with script witness.</summary>
    public TxBuilder AddDelegation(Credential delegator, string poolIdHex, IScript script, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        ICertificate cert = StakeDelegation.Create(2, delegator, Convert.FromHexString(poolIdHex));
        _certificateDirectives.Add(new CertificateDirective(cert, script, ToPlutusData(redeemer)));
        return this;
    }

    /// <summary>Register a stake credential.</summary>
    public TxBuilder AddRegisterStake(Credential credential)
    {
        _certificateDirectives.Add(new CertificateDirective(StakeRegistration.Create(0, credential)));
        return this;
    }

    /// <summary>Deregister a stake credential.</summary>
    public TxBuilder AddDeregisterStake(Credential credential)
    {
        _certificateDirectives.Add(new CertificateDirective(StakeDeregistration.Create(1, credential)));
        return this;
    }

    /// <summary>Deregister a stake credential with script witness.</summary>
    public TxBuilder AddDeregisterStake(Credential credential, IScript script, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        _certificateDirectives.Add(new CertificateDirective(StakeDeregistration.Create(1, credential), script, ToPlutusData(redeemer)));
        return this;
    }

    /// <summary>Register a pool.</summary>
    public TxBuilder AddRegisterPool(PoolRegistration poolParams)
    {
        _certificateDirectives.Add(new CertificateDirective(poolParams));
        return this;
    }

    /// <summary>Retire a pool at a given epoch.</summary>
    public TxBuilder AddRetirePool(string poolIdHex, ulong epoch)
    {
        _certificateDirectives.Add(new CertificateDirective(PoolRetirement.Create(4, Convert.FromHexString(poolIdHex), epoch)));
        return this;
    }

    // ── Withdrawals ──

    /// <summary>Withdraw staking rewards.</summary>
    public TxBuilder AddWithdrawal(string rewardAddressHex, ulong amount)
    {
        _withdrawalDirectives.Add(new WithdrawalDirective(rewardAddressHex, amount));
        return this;
    }

    /// <summary>Withdraw staking rewards with script witness.</summary>
    public TxBuilder AddWithdrawal(string rewardAddressHex, ulong amount, IScript script, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        _withdrawalDirectives.Add(new WithdrawalDirective(rewardAddressHex, amount, script, ToPlutusData(redeemer)));
        return this;
    }

    /// <summary>Withdraw staking rewards with a reference script.</summary>
    public TxBuilder AddWithdrawal(string rewardAddressHex, ulong amount, string scriptHash, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        _withdrawalDirectives.Add(new WithdrawalDirective(rewardAddressHex, amount, null, ToPlutusData(redeemer)) { ScriptRefHash = scriptHash });
        return this;
    }

    // ── Governance (Conway) ──

    /// <summary>Register a delegate representative.</summary>
    public TxBuilder AddRegisterDRep(Credential drep, ulong deposit)
    {
        _certificateDirectives.Add(new CertificateDirective(RegDrepCert.Create(16, drep, deposit, null)));
        return this;
    }

    /// <summary>Unregister a delegate representative.</summary>
    public TxBuilder AddUnregisterDRep(Credential drep, ulong refund)
    {
        _certificateDirectives.Add(new CertificateDirective(UnRegDrepCert.Create(17, drep, refund)));
        return this;
    }

    /// <summary>Update a delegate representative.</summary>
    public TxBuilder AddUpdateDRep(Credential drep)
    {
        _certificateDirectives.Add(new CertificateDirective(UpdateDrepCert.Create(18, drep, null)));
        return this;
    }

    /// <summary>Delegate voting power to a DRep.</summary>
    public TxBuilder AddVoteDelegation(Credential delegator, IDRep drep)
    {
        _certificateDirectives.Add(new CertificateDirective(VoteDelegCert.Create(9, delegator, drep)));
        return this;
    }

    /// <summary>Delegate voting power to a DRep with script witness.</summary>
    public TxBuilder AddVoteDelegation(Credential delegator, IDRep drep, IScript script, ICborType redeemer)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        _certificateDirectives.Add(new CertificateDirective(VoteDelegCert.Create(9, delegator, drep), script, ToPlutusData(redeemer)));
        return this;
    }

    /// <summary>Set voting procedures.</summary>
    public TxBuilder SetVotingProcedures(VotingProcedures votingProcedures)
    {
        _votingProcedures = votingProcedures;
        return this;
    }

    /// <summary>Add a governance proposal.</summary>
    public TxBuilder AddProposal(ProposalProcedure proposal)
    {
        _proposals.Add(proposal);
        return this;
    }

    // ── Configuration ──

    /// <summary>Set the change address (required for Complete).</summary>
    public TxBuilder SetChangeAddress(string bech32Address)
    {
        _changeAddress = bech32Address;
        return this;
    }

    /// <summary>Set validity start slot.</summary>
    public TxBuilder SetValidFrom(ulong slot)
    {
        _validFrom = slot;
        return this;
    }

    /// <summary>Set validity end slot (TTL).</summary>
    public TxBuilder SetValidUntil(ulong slot)
    {
        _validUntil = slot;
        return this;
    }

    /// <summary>Set the network ID.</summary>
    public TxBuilder SetNetworkId(int networkId)
    {
        _networkId = networkId;
        return this;
    }

    /// <summary>Add a required signer.</summary>
    public TxBuilder AddRequiredSigner(string pkhHex)
    {
        _requiredSigners.Add(pkhHex);
        return this;
    }

    /// <summary>Add a metadata entry.</summary>
    public TxBuilder AddMetadata(ulong label, ITransactionMetadatum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _metadata[label] = value;
        return this;
    }

    /// <summary>Provide a script explicitly (for witness set).</summary>
    public TxBuilder ProvideScript(IScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _providedScripts.Add(script);
        return this;
    }

    /// <summary>Provide an extraneous datum (not tied to specific output).</summary>
    public TxBuilder ProvideDatum(IPlutusData datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        _providedDatums.Add(datum);
        return this;
    }

    /// <summary>Provide specific UTxOs for collateral (optional, auto-selected if not provided).</summary>
    public TxBuilder ProvideCollateral(IEnumerable<ResolvedInput> utxos)
    {
        ArgumentNullException.ThrowIfNull(utxos);
        _collateralPool = [.. utxos];
        return this;
    }

    /// <summary>Set the coin selection strategy.</summary>
    public TxBuilder UseCoinSelector(CoinSelectionStrategy strategy)
    {
        _coinSelectionStrategy = strategy;
        return this;
    }

    /// <summary>Set the treasury value.</summary>
    public TxBuilder SetTreasuryValue(ulong treasuryValue)
    {
        _treasuryValue = treasuryValue;
        return this;
    }

    /// <summary>Set the donation amount.</summary>
    public TxBuilder SetDonation(ulong donation)
    {
        _donation = donation;
        return this;
    }

    /// <summary>Add fee padding (added to every fee calculation).</summary>
    public TxBuilder SetFeePadding(ulong padding)
    {
        _feePadding = padding;
        return this;
    }

    /// <summary>Set a minimum fee floor.</summary>
    public TxBuilder SetMinimumFee(ulong minFee)
    {
        _minimumFee = minFee;
        return this;
    }

    /// <summary>Use an external script evaluator instead of the built-in Plutus VM.</summary>
    public TxBuilder UseEvaluator(
        Func<PostMaryTransaction, IReadOnlyList<ResolvedInput>, Task<IReadOnlyList<Plutus.VM.Models.EvaluationResult>>> evaluator)
    {
        _evaluator = evaluator;
        return this;
    }

    // ── Finalization ──

    /// <summary>
    /// Completes the transaction: resolves UTxOs, performs coin selection,
    /// calculates fees, builds change output, selects collateral, evaluates scripts.
    /// Returns a balanced, ready-to-sign transaction.
    /// </summary>
    public async Task<PostMaryTransaction> Complete()
    {
        if (_changeAddress is null)
        {
            throw new InvalidOperationException("Change address is required. Call SetChangeAddress() before Complete().");
        }

        _pparams ??= await _provider.GetParametersAsync().ConfigureAwait(false);

        TransactionBuilder builder = TransactionBuilder.Create(_pparams);

        // ── Apply configuration ──
        if (_validUntil is not null)
        {
            _ = builder.SetTtl(_validUntil.Value);
        }

        if (_validFrom is not null)
        {
            _ = builder.SetValidityIntervalStart(_validFrom.Value);
        }

        if (_networkId is not null)
        {
            _ = builder.SetNetworkId(_networkId.Value);
        }

        if (_votingProcedures is not null)
        {
            _ = builder.SetVotingProcedures(_votingProcedures);
        }

        if (_treasuryValue is not null)
        {
            _ = builder.SetTreasuryValue(_treasuryValue.Value);
        }

        if (_donation is not null)
        {
            _ = builder.SetDonation(_donation.Value);
        }

        foreach (string signer in _requiredSigners)
        {
            _ = builder.AddRequiredSigner(signer);
        }

        foreach (ProposalProcedure proposal in _proposals)
        {
            _ = builder.AddProposalProcedure(proposal);
        }

        foreach (IPlutusData datum in _providedDatums)
        {
            _ = builder.AddPlutusData(datum);
        }

        foreach (KeyValuePair<ulong, ITransactionMetadatum> kv in _metadata)
        {
            _ = builder.AddMetadata(kv.Key, kv.Value);
        }

        // ── Auxiliary data hash (must be set before fee calculation) ──
        if (_metadata.Count > 0)
        {
            _ = builder.ComputeAndSetAuxDataHash();
        }

        // ── Add reference inputs ──
        foreach (ResolvedInput refInput in _referenceInputs)
        {
            _ = builder.AddReferenceInput(refInput.Outref);
        }

        // ── Add explicit inputs ──
        List<IScript> allScripts = [.. _providedScripts];

        foreach (InputDirective input in _explicitInputs)
        {
            if (input.Redeemer is not null)
            {
                (IScript script, bool isReference) = ResolveScriptForInput(input.Utxo);
                allScripts.Add(script);

                InputBuilderResult result;
                if (isReference)
                {
                    // Script is in a reference input — don't add to witness set
                    string scriptHash = script.HashHex();
                    result = new InputBuilder(input.Utxo.Outref, input.Utxo.Output)
                        .PlutusScriptRef(scriptHash, input.Redeemer, input.Datum);
                }
                else if (input.Datum is not null)
                {
                    result = new InputBuilder(input.Utxo.Outref, input.Utxo.Output)
                        .PlutusScript(script, input.Redeemer, input.Datum);
                }
                else
                {
                    result = new InputBuilder(input.Utxo.Outref, input.Utxo.Output)
                        .PlutusScriptInlineDatum(script, input.Redeemer);
                }

                _ = builder.AddInput(result);
            }
            else
            {
                _ = builder.AddInput(input.Utxo.Outref);
            }
        }

        // ── Add mints ──
        foreach (MintDirective mint in _mintDirectives)
        {
            MultiAssetMint mintValue = BuildMintValue(mint);

            if (mint.Script is not null)
            {
                allScripts.Add(mint.Script);
                _ = builder.AddMint(mintValue, mint.Script, mint.Redeemer!);
            }
            else if (mint.ScriptRefHash is not null && mint.Redeemer is not null)
            {
                // Mint via reference script — add mint only, redeemer built from directives in stabilization loop
                _ = builder.AddMint(mintValue);
            }
            else if (mint.NativeScript is not null)
            {
                _ = builder.AddNativeScript(mint.NativeScript);
                _ = builder.AddMint(mintValue);
            }
            else
            {
                _ = builder.AddMint(mintValue);
            }
        }

        // ── Add certificates ──
        foreach (CertificateDirective cert in _certificateDirectives)
        {
            if (cert.Script is not null && cert.Redeemer is not null)
            {
                allScripts.Add(cert.Script);
                _ = builder.AddCertificate(cert.Certificate, cert.Script, cert.Redeemer);
            }
            else
            {
                _ = builder.AddCertificate(cert.Certificate);
            }
        }

        // ── Add withdrawals ──
        foreach (WithdrawalDirective wd in _withdrawalDirectives)
        {
            if (wd.Script is not null && wd.Redeemer is not null)
            {
                allScripts.Add(wd.Script);
                _ = builder.AddWithdrawal(wd.RewardAddressHex, wd.Amount, wd.Script, wd.Redeemer);
            }
            else if (wd.ScriptRefHash is not null && wd.Redeemer is not null)
            {
                // Withdrawal via reference script — add withdrawal only, redeemer built from directives
                _ = builder.AddWithdrawal(wd.RewardAddressHex, wd.Amount);
            }
            else
            {
                _ = builder.AddWithdrawal(wd.RewardAddressHex, wd.Amount);
            }
        }

        // ── Resolve reference scripts for directives (for fee + data hash, NOT witness set) ──
        List<IScript> refScripts = [];
        foreach (MintDirective mint in _mintDirectives)
        {
            if (mint.ScriptRefHash is not null)
            {
                IScript? resolved = ResolveScriptFromReferenceInputs(mint.ScriptRefHash);
                if (resolved is not null)
                {
                    refScripts.Add(resolved);
                }
            }
        }

        foreach (WithdrawalDirective wd in _withdrawalDirectives)
        {
            if (wd.ScriptRefHash is not null)
            {
                IScript? resolved = ResolveScriptFromReferenceInputs(wd.ScriptRefHash);
                if (resolved is not null)
                {
                    refScripts.Add(resolved);
                }
            }
        }

        // ── Add outputs ──
        foreach (OutputDirective output in _outputDirectives)
        {
            ITransactionOutput built = BuildOutput(output, _pparams);
            _ = builder.AddOutput(built);
        }

        builder.RejectSignatures();
        builder.IntegrateWithdrawals();
        List<ResolvedInput> spending = [.. _explicitInputs.Select(input => input.Utxo)];
        List<ResolvedInput> pool = [.. _unspentOutputs.Where(candidate => !spending.Any(input => InputComparer.Compare(input.Outref, candidate.Outref) == 0)
            && !_referenceInputs.Any(input => InputComparer.Compare(input.Outref, candidate.Outref) == 0))];
        List<IScript> scripts = [.. allScripts.Concat(refScripts).Where(script => script.Version() > 0)];
        ReindexRedeemers(builder, null);
        _ = builder.SetFee(300_000);
        if (builder.Redeemers is not null)
        {
            _ = await EvaluateScripts(builder, builder.Build(), DistinctInputs(spending, _referenceInputs)).ConfigureAwait(false);
        }
        return await Balance(builder, _changeAddress, spending, _referenceInputs, _collateralPool ?? [.. pool], scripts, pool, evaluate: true)
            .ConfigureAwait(false);
    }

    internal async Task<PostMaryTransaction> Finalize(
        TransactionBuilder builder, string changeAddress, List<ResolvedInput> resolvedInputs, List<ResolvedInput> allUtxos,
        List<ResolvedInput> collateralPool, bool evaluate = true)
    {
        builder.RejectSignatures();
        _pparams ??= await _provider.GetParametersAsync().ConfigureAwait(false);
        builder.IntegrateWithdrawals();
        builder.IntegrateRedeemerSet();
        List<IScript> scripts = [];
        if (builder.Redeemers is not null)
        {
            foreach ((ReadOnlyMemory<byte> data, int version) in builder.PlutusScripts)
            {
                scripts.Add(version switch
                {
                    1 => PlutusV1Script.Create(1, data),
                    2 => PlutusV2Script.Create(2, data),
                    3 => PlutusV3Script.Create(3, data),
                    _ => throw new NotSupportedException($"Unsupported Plutus version {version}.")
                });
            }
            foreach (ResolvedInput input in resolvedInputs.Concat((builder.ReferenceInputs ?? []).Select(reference => TransactionBuilderExtensions.Resolve(reference, allUtxos))))
            {
                if (input.Output is PostAlonzoTransactionOutput { ScriptRef: { } reference } && reference.Deserialize<IScript>() is { } script && script.Version() > 0)
                {
                    scripts.Add(script);
                }
            }
        }
        return await Balance(builder, changeAddress, resolvedInputs, allUtxos, collateralPool, scripts, null, evaluate).ConfigureAwait(false);
    }

    private async Task<PostMaryTransaction> Balance(TransactionBuilder builder, string changeAddress,
        List<ResolvedInput> spending, List<ResolvedInput> allUtxos, List<ResolvedInput> collateralPool,
        List<IScript> scripts, List<ResolvedInput>? pool, bool evaluate)
    {
        int structureLimit = (pool?.Count ?? 0) + collateralPool.Count + 2;
        int repairs = 0;
        List<(int Tag, ulong Index, ulong Mem, ulong Steps)[]> seen = [];
        (ulong budget, ulong deficit) = TransactionBuilderExtensions.BuildChangeOutput(builder, spending, changeAddress, includeCertificateCoins: pool is null);
        if (builder.Redeemers is not null && builder.CollateralInputs is null
            && (collateralPool.Count == 0 || !TransactionBuilderExtensions.TrySelectCollateral(builder, 0, collateralPool)))
        {
            throw new InvalidOperationException("Insufficient collateral funds.");
        }
        SetIntegrityHash();
        while (repairs <= structureLimit)
        {
            if (deficit > 0)
            {
                if (!AddSpending(deficit))
                {
                    throw new InvalidOperationException("Insufficient spending funds.");
                }
                seen.Clear();
                repairs++;
                continue;
            }

            List<ResolvedInput> prices = [.. spending, .. allUtxos, .. collateralPool];
            FeeInputs inputs = new(Budget: budget, Padding: _feePadding, MinimumFee: _minimumFee ?? 0);
            FeeResult fee = TransactionBuilderExtensions.PriceStructure(builder, prices, inputs, 0, out PostMaryTransaction result);
            if (fee.Status != FeeStatus.Balanced)
            {
                TransactionBuilderExtensions.RejectFixedFee(fee);
                bool changed = fee.Status.HasFlag(FeeStatus.SpendingShort) &&
                    (AddSpending(fee.SpendingRequest) || TransactionBuilderExtensions.TryDropChange(builder));
                if (fee.Status.HasFlag(FeeStatus.CollateralShort) && collateralPool.Count > 0)
                {
                    changed |= TransactionBuilderExtensions.TrySelectCollateral(builder, fee.FeeHint, collateralPool);
                }
                if (!changed)
                {
                    throw TransactionBuilderExtensions.InsufficientFunds(fee);
                }
                seen.Clear();
                repairs++;
                continue;
            }

            if (!evaluate || builder.Redeemers is null)
            {
                return result;
            }
            seen.Add(UnitVector(builder.Redeemers));
            if (!await EvaluateScripts(builder, result, DistinctInputs(spending, allUtxos)).ConfigureAwait(false))
            {
                return result;
            }
            (int, ulong, ulong, ulong)[] next = UnitVector(builder.Redeemers!);
            if (seen.Count == 4 || seen.Any(previous => previous.AsSpan().SequenceEqual(next)))
            {
                throw new InvalidOperationException("script units did not settle");
            }
            SetIntegrityHash();
        }
        throw new InvalidOperationException("Fee structure did not settle.");

        void SetIntegrityHash()
        {
            if (builder.Redeemers is not null && scripts.Count > 0)
            {
                _ = builder.ComputeAndSetScriptDataHash(scripts);
            }
        }

        bool AddSpending(ulong request)
        {
            if (pool is not { Count: > 0 })
            {
                return false;
            }
            List<ResolvedInput> remaining = [.. pool.Where(candidate => !(builder.CollateralInputs ?? []).Any(input => InputComparer.Compare(input, candidate.Outref) == 0))];
            // Collateral can overlap spending under the ledger rules, but first use any other candidates.
            if (remaining.Count == 0)
            {
                remaining.AddRange(pool);
            }
            List<ResolvedInput> selected = request == 0 || remaining.Aggregate((UInt128)0, (sum, input) => sum + input.Output.Amount().Lovelace()) < request
                ? [remaining.MaxBy(input => input.Output.Amount().Lovelace())!]
                : SelectForRequest(remaining, request);
            if (selected.Count == 0)
            {
                return false;
            }
            List<TransactionInput> previous = [.. builder.Inputs.Order(InputComparer)];
            foreach (ResolvedInput input in selected)
            {
                _ = builder.AddInput(input.Outref);
                spending.Add(input);
                _ = pool.Remove(input);
            }
            if (builder.Redeemers is not null)
            {
                ReindexRedeemers(builder, previous);
                SetIntegrityHash();
            }
            (budget, deficit) = TransactionBuilderExtensions.BuildChangeOutput(builder, spending, changeAddress, includeCertificateCoins: pool is null);
            return true;
        }
    }

    private List<ResolvedInput> SelectForRequest(List<ResolvedInput> candidates, ulong request)
    {
        try
        {
            return CoinSelectionUtil.Select(candidates, [Lovelace.FromAmount(request)], _coinSelectionStrategy).Inputs;
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static (int Tag, ulong Index, ulong Mem, ulong Steps)[] UnitVector(IRedeemers redeemers) =>
        [.. TransactionBuilderExtensions.RedeemerUnits(redeemers).Order()];

    internal static IComparer<TransactionInput> InputComparer { get; } = Comparer<TransactionInput>.Create(static (a, b) =>
    {
        int order = a.TransactionId.Span.SequenceCompareTo(b.TransactionId.Span);
        return order == 0 ? a.Index.CompareTo(b.Index) : order;
    });

    // ── Type Conversion ──

    private static List<ResolvedInput> DistinctInputs(List<ResolvedInput> first, List<ResolvedInput> second)
    {
        SortedSet<TransactionInput> seen = [with(InputComparer)];
        List<ResolvedInput> result = [];
        foreach (ResolvedInput input in first.Concat(second))
        {
            if (seen.Add(input.Outref))
            {
                result.Add(input);
            }
        }
        return result;
    }

    internal static IPlutusData ToPlutusData(ICborType value)
    {
        if (value is IPlutusData plutusData)
        {
            return plutusData;
        }

        // Use Raw bytes if available (deserialized types have this populated)
        ReadOnlyMemory<byte> cbor = value.Raw.IsEmpty
            ? SerializeUsingRuntimeType(value)
            : value.Raw;
        return CborSerializer.Deserialize<IPlutusData>(cbor);
    }

    private static byte[] SerializeUsingRuntimeType(ICborType value)
    {
        System.Reflection.MethodInfo serializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Serialize))!
            .MakeGenericMethod(value.GetType());
        try
        {
            return (byte[])serializeMethod.Invoke(null, [value])!;
        }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error.InnerException);
            throw;
        }
    }

    // ── Script Resolution ──

    private (IScript Script, bool IsReference) ResolveScriptForInput(ResolvedInput utxo)
    {
        byte[] scriptHash = ExtractScriptHash(utxo.Output);
        string hashHex = Convert.ToHexString(scriptHash);

        // 1. Check reference inputs for matching script (preferred — avoids witness bloat)
        foreach (ResolvedInput refInput in _referenceInputs)
        {
            if (refInput.Output is PostAlonzoTransactionOutput refOutput && refOutput.ScriptRef is not null)
            {
                IScript refScript = refOutput.ScriptRef.Deserialize<IScript>();
                if (refScript.HashHex().Equals(hashHex, StringComparison.OrdinalIgnoreCase))
                {
                    return (refScript, true);
                }
            }
        }

        // 2. Check UTxO output for inline script reference
        if (utxo.Output is PostAlonzoTransactionOutput postAlonzo && postAlonzo.ScriptRef is not null)
        {
            return (postAlonzo.ScriptRef.Deserialize<IScript>(), false);
        }

        // 3. Check provided scripts
        foreach (IScript provided in _providedScripts)
        {
            if (provided.HashHex().Equals(hashHex, StringComparison.OrdinalIgnoreCase))
            {
                return (provided, false);
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve script for input {Convert.ToHexString(utxo.Outref.TransactionId.Span)}#{utxo.Outref.Index}. " +
            "Provide the script via ProvideScript() or AddReferenceInput().");
    }

    /// <summary>
    /// Evaluates scripts using the external evaluator if provided, otherwise the built-in Plutus VM.
    /// </summary>
    private async Task<bool> EvaluateScripts(TransactionBuilder builder, PostMaryTransaction transaction, List<ResolvedInput> resolvedInputs)
    {
        IReadOnlyList<Plutus.VM.Models.EvaluationResult> evaluated = _evaluator is not null
            ? await _evaluator(transaction, resolvedInputs).ConfigureAwait(false)
            : ScriptContextBuilder.EvaluateTx((ConwayTransactionBody)transaction.Body, (PostAlonzoTransactionWitnessSet)transaction.Witnesses,
                resolvedInputs, SlotNetworkConfig.FromNetworkType(_provider.NetworkType), builder.Pparams?.CostModelsForScriptLanguage);
        return TransactionBuilderExtensions.UpdateRedeemersWithEvalResults(builder, evaluated);
    }

    // Reuse the codec's ledger comparer even for newly created redeemer keys.
    internal static readonly IEqualityComparer<RedeemerKey> RedeemerKeyComparer = RedeemerMap.Create([]).Value.Comparer;

    /// <summary>
    /// Each distinct key's place among the keys in the given order, by the key's hex in any case:
    /// the index a redeemer for that key points at (the ledger's indexOf on a body map or set).
    /// </summary>
    internal static Dictionary<string, int> RedeemerIndices(IEnumerable<ReadOnlyMemory<byte>> keys, IComparer<ReadOnlyMemory<byte>> order)
    {
        Dictionary<string, int> indices = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (ReadOnlyMemory<byte> key in keys.Order(order))
        {
            _ = indices.TryAdd(Convert.ToHexString(key.Span), indices.Count);
        }

        return indices;
    }

    /// <summary>
    /// Builds a complete redeemer map from ALL directive lists (spend, mint, withdrawal, cert).
    /// This avoids using RedeemerSet which gets bypassed when SetRedeemers is called directly.
    /// </summary>
    private void ReindexRedeemers(TransactionBuilder builder, List<TransactionInput>? previous)
    {
        List<TransactionInput> sorted = [.. builder.Inputs.Order(InputComparer)];
        RedeemerMap? old = builder.Redeemers is RedeemerMap map ? map : null;
        ExUnits placeholderExUnits = ExUnits.Create(14_000_000, 10_000_000_000);
        Dictionary<RedeemerKey, RedeemerValue> redeemerMap = [with(RedeemerKeyComparer)];

        // ── Tag 0: Spend redeemers (sorted by tx_hash + index) ──
        foreach (InputDirective directive in _explicitInputs)
        {
            if (directive.Redeemer is null)
            {
                continue;
            }

            int sortedIndex = sorted.BinarySearch(directive.Utxo.Outref, InputComparer);
            if (sortedIndex >= 0)
            {
                int oldIndex = previous?.BinarySearch(directive.Utxo.Outref, InputComparer) ?? sortedIndex;
                redeemerMap[RedeemerKey.Create(0, (ulong)sortedIndex)] =
                    RedeemerValue.Create(directive.Redeemer, KeepUnits(0, oldIndex));
            }
        }

        // ── Tag 1: Mint redeemers (the policy's place in the mint, in byte order) ──
        Dictionary<string, int> mintIndices = RedeemerIndices(
            builder.Mint is { } minted ? minted.Value.Keys : [], ByteMemoryComparer.Instance);
        foreach (MintDirective mint in _mintDirectives)
        {
            if (mint.Redeemer is not null && mintIndices.TryGetValue(mint.PolicyHex, out int mintIndex))
            {
                redeemerMap[RedeemerKey.Create(1, (ulong)mintIndex)] =
                    RedeemerValue.Create(mint.Redeemer, KeepUnits(1, mintIndex));
            }
        }

        // ── Tag 2: Certificate redeemers (by position in cert list) ──
        for (int i = 0; i < _certificateDirectives.Count; i++)
        {
            CertificateDirective cert = _certificateDirectives[i];
            if (cert.Redeemer is not null)
            {
                redeemerMap[RedeemerKey.Create(2, (ulong)i)] =
                    RedeemerValue.Create(cert.Redeemer, KeepUnits(2, i));
            }
        }

        // ── Tag 3: Withdrawal redeemers (the account's place in the ledger's order) ──
        // The body gets its withdrawals only when first built, so count the directives' accounts.
        Dictionary<string, int> withdrawalIndices = RedeemerIndices(
            _withdrawalDirectives.Select(w => (ReadOnlyMemory<byte>)Convert.FromHexString(w.RewardAddressHex)),
            RewardAccountComparer.Instance);
        foreach (WithdrawalDirective wd in _withdrawalDirectives)
        {
            if (wd.Redeemer is not null && withdrawalIndices.TryGetValue(wd.RewardAddressHex, out int wdIndex))
            {
                redeemerMap[RedeemerKey.Create(3, (ulong)wdIndex)] =
                    RedeemerValue.Create(wd.Redeemer, KeepUnits(3, wdIndex));
            }
        }

        if (redeemerMap.Count > 0)
        {
            _ = builder.SetRedeemers(RedeemerMap.Create(redeemerMap));
        }

        ExUnits KeepUnits(int tag, int index)
        {
            if (index >= 0 && old is { } existing)
            {
                foreach ((RedeemerKey key, RedeemerValue value) in existing.Value)
                {
                    if (key.Tag == tag && key.Index == (ulong)index)
                    {
                        return value.ExUnits;
                    }
                }
            }
            return placeholderExUnits;
        }
    }

    private IScript? ResolveScriptFromReferenceInputs(string scriptHashHex)
    {
        foreach (ResolvedInput refInput in _referenceInputs)
        {
            if (refInput.Output is PostAlonzoTransactionOutput refOutput && refOutput.ScriptRef is not null)
            {
                IScript refScript = CborSerializer.Deserialize<IScript>(refOutput.ScriptRef.GetValue());
                if (refScript.HashHex().Equals(scriptHashHex, StringComparison.OrdinalIgnoreCase))
                {
                    return refScript;
                }
            }
        }
        return null;
    }

    private static byte[] ExtractScriptHash(ITransactionOutput output)
    {
        ReadOnlyMemory<byte> addressBytes = output switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Address.Value,
            PostAlonzoTransactionOutput postAlonzo => postAlonzo.Address.Value,
            _ => throw new InvalidOperationException("Unsupported output type")
        };

        // Payment credential is bytes 1..29 of the address
        return addressBytes.Slice(1, 28).ToArray();
    }

    // ── Output Building ──

    private static ITransactionOutput BuildOutput(OutputDirective directive, ProtocolParams pparams)
    {
        if (directive.RawOutput is not null)
        {
            return directive.RawOutput;
        }

        OutputBuilder ob = directive.AddressBytes is not null
            ? new OutputBuilder(directive.AddressBytes, directive.Amount!)
            : new OutputBuilder(directive.Bech32Address!, directive.Amount!);

        if (directive.DatumCbor is not null)
        {
            IPlutusData datumValue = CborSerializer.Deserialize<IPlutusData>(directive.DatumCbor);
            _ = ob.WithInlineDatum(datumValue);
        }

        if (directive.ScriptRef is not null)
        {
            _ = ob.WithScriptRef(directive.ScriptRef);
        }

        ITransactionOutput built = ob.Build();

        if (pparams.AdaPerUTxOByte is { } cost)
        {
            ulong coin = FeeEngine.LeastValidCoin(built, cost);
            if (coin != built.Amount().Lovelace())
            {
                ob.Amount = ob.Amount is LovelaceWithMultiAsset assets
                    ? LovelaceWithMultiAsset.Create(coin, assets.MultiAsset) : Lovelace.FromAmount(coin);
                built = ob.Build();
            }
        }

        return built;
    }

    private static MultiAssetMint BuildMintValue(MintDirective mint)
    {
        MintBuilder mb = MintBuilder.Create();
        foreach (KeyValuePair<string, long> asset in mint.Assets)
        {
            _ = mb.AddToken(mint.PolicyHex, asset.Key, asset.Value);
        }

        return mb.Build();
    }

    // ── Internal Directive Types ──

    private sealed record InputDirective(ResolvedInput Utxo, IPlutusData? Redeemer, IPlutusData? Datum);

    private sealed class OutputDirective
    {
        public string? Bech32Address { get; }
        public byte[]? AddressBytes { get; }
        public IValue? Amount { get; }
        public byte[]? DatumCbor { get; }
        public IScript? ScriptRef { get; }
        public ITransactionOutput? RawOutput { get; }

        public OutputDirective(string bech32Address, IValue amount, byte[]? datumCbor = null, IScript? scriptRef = null)
        {
            Bech32Address = bech32Address;
            Amount = amount;
            DatumCbor = datumCbor;
            ScriptRef = scriptRef;
        }

        public OutputDirective(byte[] addressBytes, IValue amount, byte[]? datumCbor = null, IScript? scriptRef = null)
        {
            AddressBytes = addressBytes;
            Amount = amount;
            DatumCbor = datumCbor;
            ScriptRef = scriptRef;
        }

        public OutputDirective(ITransactionOutput rawOutput) => RawOutput = rawOutput;
    }

    private sealed record MintDirective(
        string PolicyHex,
        Dictionary<string, long> Assets,
        IScript? Script,
        INativeScript? NativeScript,
        IPlutusData? Redeemer)
    {
        public string? ScriptRefHash { get; init; }
    }

    private sealed record CertificateDirective(
        ICertificate Certificate,
        IScript? Script = null,
        IPlutusData? Redeemer = null);

    private sealed record WithdrawalDirective(
        string RewardAddressHex,
        ulong Amount,
        IScript? Script = null,
        IPlutusData? Redeemer = null)
    {
        public string? ScriptRefHash { get; init; }
    }
}
