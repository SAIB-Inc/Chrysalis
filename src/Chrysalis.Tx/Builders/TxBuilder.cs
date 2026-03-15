using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Extensions;
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
    private const int MaxStabilizationIterations = 10;
    private const long FeeConvergenceTolerance = 1000;

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
                IScript script = ResolveScriptForInput(input.Utxo);
                allScripts.Add(script);

                InputBuilderResult result = input.Datum is not null
                    ? new InputBuilder(input.Utxo.Outref, input.Utxo.Output)
                        .PlutusScript(script, input.Redeemer, input.Datum)
                    : new InputBuilder(input.Utxo.Outref, input.Utxo.Output)
                        .PlutusScriptInlineDatum(script, input.Redeemer);

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
            else
            {
                _ = builder.AddWithdrawal(wd.RewardAddressHex, wd.Amount);
            }
        }

        // ── Add outputs ──
        foreach (OutputDirective output in _outputDirectives)
        {
            ITransactionOutput built = BuildOutput(output, _pparams);
            _ = builder.AddOutput(built);
        }

        // ── Stabilization loop ──
        bool hasScripts = allScripts.Count > 0 || builder.RedeemerSet.HasRedeemers;
        List<ResolvedInput> selectedInputs = [];

        // Evaluate scripts if present
        if (hasScripts)
        {
            List<ResolvedInput> allResolvedInputs = [.. _explicitInputs.Select(i => i.Utxo), .. _referenceInputs];
            SlotNetworkConfig slotConfig = SlotNetworkConfig.FromNetworkType(_provider.NetworkType);
            _ = builder.Evaluate(allResolvedInputs, slotConfig);
        }

        _ = builder.SetFee(300_000);

        ulong previousFee = 0;
        for (int iteration = 0; iteration < MaxStabilizationIterations; iteration++)
        {
            // Calculate deficit
            ulong totalInputLovelace = _explicitInputs.Aggregate(0UL, (sum, i) => sum + i.Utxo.Output.Amount().Lovelace())
                + selectedInputs.Aggregate(0UL, (sum, i) => sum + i.Output.Amount().Lovelace());

            ulong totalOutputLovelace = 0;
            for (int i = 0; i < builder.Outputs.Count; i++)
            {
                totalOutputLovelace += builder.Outputs[i].Amount().Lovelace();
            }

            ulong totalWithdrawals = _withdrawalDirectives.Aggregate(0UL, (sum, w) => sum + w.Amount);
            ulong available = totalInputLovelace + totalWithdrawals;
            ulong required = totalOutputLovelace + builder.Fee;

            // Coin selection if deficit
            if (required > available && _unspentOutputs.Count > 0)
            {
                List<ResolvedInput> pool = [.. _unspentOutputs
                    .Where(u => !_explicitInputs.Any(e => e.Utxo.Outref.Equals(u.Outref)))
                    .Where(u => !selectedInputs.Any(s => s.Outref.Equals(u.Outref)))];

                if (pool.Count > 0)
                {
                    ulong deficit = required - available;
                    CoinSelectionResult selection = CoinSelectionUtil.Select(
                        pool,
                        [Lovelace.Create(deficit)],
                        _coinSelectionStrategy);

                    foreach (ResolvedInput selected in selection.Inputs)
                    {
                        _ = builder.AddInput(selected.Outref);
                        selectedInputs.Add(selected);
                    }
                }
            }

            // Rebuild available after coin selection
            ulong updatedInputLovelace = _explicitInputs.Aggregate(0UL, (sum, i) => sum + i.Utxo.Output.Amount().Lovelace())
                + selectedInputs.Aggregate(0UL, (sum, i) => sum + i.Output.Amount().Lovelace());
            ulong updatedAvailable = updatedInputLovelace + totalWithdrawals;

            // Remove old change output if present
            if (builder.ChangeOutputIndex is not null)
            {
                List<ITransactionOutput> outputs = [.. builder.Outputs];
                outputs.RemoveAt(builder.ChangeOutputIndex.Value);
                _ = builder.SetOutputs(outputs);
                builder.ChangeOutputIndex = null;
                builder.ChangeOutput = null;
            }

            // Recalculate outputs total (without change)
            ulong outputsWithoutChange = 0;
            for (int i = 0; i < builder.Outputs.Count; i++)
            {
                outputsWithoutChange += builder.Outputs[i].Amount().Lovelace();
            }

            // Build change output (ADA + native assets)
            ulong surplus = updatedAvailable > outputsWithoutChange + builder.Fee
                ? updatedAvailable - outputsWithoutChange - builder.Fee
                : 0;

            if (surplus > 0)
            {
                IValue changeValue = ComputeChangeValue(surplus, _explicitInputs, selectedInputs, builder.Outputs, builder.Mint);
                _ = builder.AddOutput(_changeAddress, changeValue, isChange: true);
            }

            // Calculate fee from draft
            PostMaryTransaction draftTx = builder.Build();
            byte[] draftBytes = CborSerializer.Serialize(draftTx);
            ulong fee = FeeUtil.CalculateFeeWithWitness(
                (ulong)draftBytes.Length,
                _pparams.MinFeeA!.Value,
                _pparams.MinFeeB!.Value,
                1);

            if (hasScripts && builder.Redeemers is not null)
            {
                RationalNumber memPrice = new(
                    _pparams.ExecutionCosts!.Value.MemPrice.Numerator,
                    _pparams.ExecutionCosts.Value.MemPrice.Denominator);
                RationalNumber stepPrice = new(
                    _pparams.ExecutionCosts.Value.StepPrice.Numerator,
                    _pparams.ExecutionCosts.Value.StepPrice.Denominator);
                fee += FeeUtil.CalculateScriptExecutionFee(builder.Redeemers, stepPrice, memPrice);
            }

            if (Math.Abs((long)fee - (long)previousFee) < FeeConvergenceTolerance)
            {
                _ = builder.SetFee(fee);
                break;
            }

            previousFee = fee;
            _ = builder.SetFee(fee);
        }

        // ── Collateral ──
        if (hasScripts)
        {
            List<ResolvedInput> collateralPool = _collateralPool ?? [.. _unspentOutputs, .. selectedInputs];
            _ = builder.CalculateFee(allScripts, builder.Fee, 1, collateralPool);
        }

        // ── Auxiliary data hash ──
        if (_metadata.Count > 0)
        {
            builder.IntegrateRedeemerSet();
            PostMaryTransaction preFinal = builder.Build();
            if (preFinal.AuxiliaryData is not null)
            {
                byte[] auxBytes = CborSerializer.Serialize(preFinal.AuxiliaryData);
                _ = builder.SetAuxiliaryDataHash(Chrysalis.Wallet.Utils.HashUtil.Blake2b256(auxBytes));
            }
        }

        return builder.Build();
    }

    // ── Change Value Computation ──

    private static IValue ComputeChangeValue(
        ulong surplusLovelace,
        List<InputDirective> explicitInputs,
        List<ResolvedInput> selectedInputs,
        IReadOnlyList<ITransactionOutput> outputs,
        MultiAssetMint? mint)
    {
        // Aggregate all native assets from inputs
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, long>> assetBalance = new(ReadOnlyMemoryComparer.Instance);

        foreach (InputDirective input in explicitInputs)
        {
            AddAssetsFromValue(assetBalance, input.Utxo.Output.Amount(), positive: true);
        }

        foreach (ResolvedInput input in selectedInputs)
        {
            AddAssetsFromValue(assetBalance, input.Output.Amount(), positive: true);
        }

        // Subtract native assets going to outputs
        for (int i = 0; i < outputs.Count; i++)
        {
            AddAssetsFromValue(assetBalance, outputs[i].Amount(), positive: false);
        }

        // Add minted assets
        if (mint is not null)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> policy in mint.Value.Value)
            {
                if (!assetBalance.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, long>? bundle))
                {
                    bundle = new(ReadOnlyMemoryComparer.Instance);
                    assetBalance[policy.Key] = bundle;
                }

                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> token in policy.Value.Value)
                {
                    bundle[token.Key] = bundle.TryGetValue(token.Key, out long existing) ? existing + token.Value : token.Value;
                }
            }
        }

        // Build change value from remaining positive balances
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> changeAssets = new(ReadOnlyMemoryComparer.Instance);
        foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, long>> policy in assetBalance)
        {
            Dictionary<ReadOnlyMemory<byte>, ulong> positiveTokens = new(ReadOnlyMemoryComparer.Instance);
            foreach (KeyValuePair<ReadOnlyMemory<byte>, long> token in policy.Value)
            {
                if (token.Value > 0)
                {
                    positiveTokens[token.Key] = (ulong)token.Value;
                }
            }

            if (positiveTokens.Count > 0)
            {
                changeAssets[policy.Key] = TokenBundleOutput.Create(positiveTokens);
            }
        }

        if (changeAssets.Count > 0)
        {
            return LovelaceWithMultiAsset.Create(surplusLovelace, MultiAssetOutput.Create(changeAssets));
        }

        return Lovelace.Create(surplusLovelace);
    }

    private static void AddAssetsFromValue(
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, long>> balance,
        IValue value,
        bool positive)
    {
        if (value is not LovelaceWithMultiAsset multiAsset)
        {
            return;
        }

        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policy in multiAsset.MultiAsset.Value)
        {
            if (!balance.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, long>? bundle))
            {
                bundle = new(ReadOnlyMemoryComparer.Instance);
                balance[policy.Key] = bundle;
            }

            foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policy.Value.Value)
            {
                long delta = positive ? (long)token.Value : -(long)token.Value;
                bundle[token.Key] = bundle.TryGetValue(token.Key, out long existing) ? existing + delta : delta;
            }
        }
    }

    // ── Type Conversion ──

    private static IPlutusData ToPlutusData(ICborType value)
    {
        if (value is IPlutusData plutusData)
        {
            return plutusData;
        }

        // Use Raw bytes if available (deserialized types have this populated)
        byte[] cbor = value.Raw.Length > 0
            ? value.Raw.ToArray()
            : SerializeUsingRuntimeType(value);
        return CborSerializer.Deserialize<IPlutusData>(cbor);
    }

    private static byte[] SerializeUsingRuntimeType(ICborType value)
    {
        // Use dynamic dispatch to call Serialize with the concrete runtime type
        System.Reflection.MethodInfo serializeMethod = typeof(CborSerializer)
            .GetMethod(nameof(CborSerializer.Serialize))!
            .MakeGenericMethod(value.GetType());
        return (byte[])serializeMethod.Invoke(null, [value])!;
    }

    // ── Script Resolution ──

    private IScript ResolveScriptForInput(ResolvedInput utxo)
    {
        // 1. Check UTxO output for inline script reference
        if (utxo.Output is PostAlonzoTransactionOutput postAlonzo && postAlonzo.ScriptRef is not null)
        {
            return CborSerializer.Deserialize<IScript>(postAlonzo.ScriptRef.Value);
        }

        // 2. Check reference inputs for matching script
        byte[] scriptHash = ExtractScriptHash(utxo.Output);
        foreach (ResolvedInput refInput in _referenceInputs)
        {
            if (refInput.Output is PostAlonzoTransactionOutput refOutput && refOutput.ScriptRef is not null)
            {
                IScript refScript = refOutput.ScriptRef.Deserialize<IScript>();
                if (refScript.HashHex().Equals(Convert.ToHexString(scriptHash), StringComparison.OrdinalIgnoreCase))
                {
                    return refScript;
                }
            }
        }

        // 3. Check provided scripts
        string hashHex = Convert.ToHexString(scriptHash);
        foreach (IScript provided in _providedScripts)
        {
            if (provided.HashHex().Equals(hashHex, StringComparison.OrdinalIgnoreCase))
            {
                return provided;
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve script for input {Convert.ToHexString(utxo.Outref.TransactionId.Span)}#{utxo.Outref.Index}. " +
            "Provide the script via ProvideScript() or AddReferenceInput().");
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

        // Enforce min ADA
        if (pparams.AdaPerUTxOByte is not null)
        {
            ulong adaPerByte = (ulong)pparams.AdaPerUTxOByte;
            byte[] outputBytes = CborSerializer.Serialize(built);
            ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerByte, outputBytes);
            ulong currentLovelace = built.Amount().Lovelace();

            if (currentLovelace < minLovelace)
            {
                IValue newAmount = directive.Amount switch
                {
                    LovelaceWithMultiAsset lma => LovelaceWithMultiAsset.Create(minLovelace, lma.MultiAsset),
                    _ => Lovelace.Create(minLovelace)
                };

                ob = directive.AddressBytes is not null
                    ? new OutputBuilder(directive.AddressBytes, newAmount)
                    : new OutputBuilder(directive.Bech32Address!, newAmount);

                if (directive.DatumCbor is not null)
                {
                    ob.SetDatumOption(InlineDatumOption.Create(1, new CborEncodedValue(directive.DatumCbor)));
                }

                if (directive.ScriptRef is not null)
                {
                    _ = ob.WithScriptRef(directive.ScriptRef);
                }

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

        public OutputDirective(ITransactionOutput rawOutput)
        {
            RawOutput = rawOutput;
        }
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
        IPlutusData? Redeemer = null);
}
