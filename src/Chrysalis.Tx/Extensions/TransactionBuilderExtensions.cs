using System.Buffers.Binary;
using Chrysalis.Codec.Extensions.Cardano.Core.Certificates;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for fee calculation and script evaluation on the transaction builder.
/// </summary>
public static class TransactionBuilderExtensions
{
    /// <summary>
    /// Sets an exact fee. Without any resolved UTxOs, assumes no input carries a reference script and
    /// that <paramref name="mockWitnessFee"/> VKey witnesses sign. With UTxOs, that count is a floor.
    /// A nonzero <paramref name="defaultFee"/> is fixed and must cover its own minimum.
    /// </summary>
    public static TransactionBuilder CalculateFee(
        this TransactionBuilder builder, List<IScript> scripts, ulong defaultFee = 0, int mockWitnessFee = 1,
        List<ResolvedInput>? availableInputs = null, string? changeAddress = null,
        List<ResolvedInput>? resolvedInputs = null, List<ResolvedInput>? referenceInputs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentOutOfRangeException.ThrowIfNegative(mockWitnessFee);
        builder.RejectSignatures();
        builder.IntegrateWithdrawals();
        builder.IntegrateRedeemerSet();
        _ = builder.ComputeAndSetAuxDataHash();
        if (builder.Redeemers is not null && scripts.Count > 0)
        {
            _ = builder.ComputeAndSetScriptDataHash(scripts);
        }

        List<ResolvedInput>? resolutions = resolvedInputs is null && availableInputs is null && referenceInputs is null ? null
            : [.. resolvedInputs ?? [], .. availableInputs ?? [], .. referenceInputs ?? []];
        ulong? budget = builder.ChangeOutputIndex is { } i ? builder.Outputs[i].Amount().Lovelace() : null;
        if (changeAddress is not null && resolvedInputs is not null)
        {
            List<ResolvedInput> spending = [.. builder.Inputs.Select(input => Resolve(input, resolutions!))];
            (ulong surplus, ulong deficit) = BuildChangeOutput(builder, spending, changeAddress);
            if (deficit != 0)
            {
                throw new InvalidOperationException("Insufficient spending funds.");
            }
            budget = surplus;
        }
        ulong? fixedFee = defaultFee == 0 ? null : defaultFee;
        if (fixedFee.HasValue && budget == fixedFee)
        {
            _ = TryDropChange(builder);
        }
        if (builder.Redeemers is not null && builder.CollateralInputs is null && availableInputs is { Count: > 0 }
            && !TrySelectCollateral(builder, 0, availableInputs))
        {
            throw new InvalidOperationException("Insufficient collateral funds.");
        }

        int limit = (availableInputs?.Count ?? 0) + 3;
        for (int round = 0; round < limit; round++)
        {
            FeeResult fee = PriceStructure(builder, resolutions, new FeeInputs(Budget: budget, FixedFee: fixedFee), mockWitnessFee, out _);
            if (fee.Status == FeeStatus.Balanced)
            {
                return builder;
            }
            RejectFixedFee(fee);
            bool changed = fee.Status.HasFlag(FeeStatus.SpendingShort) && TryDropChange(builder, fixedFee);
            if (fee.Status.HasFlag(FeeStatus.CollateralShort) && availableInputs is { Count: > 0 })
            {
                changed |= TrySelectCollateral(builder, fixedFee ?? fee.FeeHint, availableInputs);
            }
            if (!changed)
            {
                throw InsufficientFunds(fee);
            }
        }
        throw new InvalidOperationException("Fee structure did not settle.");
    }

    internal static FeeResult PriceStructure(TransactionBuilder builder, IReadOnlyCollection<ResolvedInput>? resolutions,
        FeeInputs inputs, int minimumKeys, out PostMaryTransaction result)
    {
        FeeResult solved = FeeSolver.Solve(PrepareStructure(builder, resolutions, inputs, minimumKeys));
        result = default;
        if (solved.Status != FeeStatus.Balanced)
        {
            return solved;
        }
        _ = builder.SetFee(solved.Fee);
        if (builder.ChangeOutputIndex is { } index)
        {
            List<ITransactionOutput> outputs = [.. builder.Outputs];
            outputs[index] = ChangeCoin(outputs[index], solved.Change);
            builder.ChangeOutput = outputs[index];
            _ = builder.SetOutputs(outputs);
        }
        if (builder.CollateralInputs is { Count: > 0 } && builder.CollateralReturn is { } returned
            && (resolutions is not null || builder.TotalCollateral.HasValue))
        {
            _ = builder.SetTotalCollateral(solved.TotalCollateral).SetCollateralReturn(ChangeCoin(returned, solved.CollateralReturn));
        }
        result = builder.Build();
        FeeSolver.Check(result, solved);
        return solved;
    }

    internal static FeeDraft PrepareStructure(TransactionBuilder builder, IReadOnlyCollection<ResolvedInput>? resolutions,
        FeeInputs inputs, int minimumKeys)
    {
        ProtocolParams pp = builder.Pparams ?? throw new InvalidOperationException("Pricing requires protocol parameters.");
        WitnessRequirements requirements = resolutions is null ? new() : WitnessRequirements.FromBuilder(builder, resolutions);
        ulong referenceSize = resolutions is null ? 0 : FeeEngine.ReferenceScriptSize(builder.Inputs, builder.ReferenceInputs ?? [], resolutions);
        ulong? collateral = builder.CollateralInputs is { Count: > 0 } collateralInputs
            ? resolutions is not null ? SumCoins(collateralInputs.Select(input => Resolve(input, resolutions)))
                : builder.TotalCollateral is { } total ? checked(total + (builder.CollateralReturn?.Amount().Lovelace() ?? 0)) : null
            : null;
        if (builder.ChangeOutputIndex is null && inputs.Budget.HasValue && inputs.FixedFee.HasValue && inputs.Budget != inputs.FixedFee)
        {
            throw new InvalidOperationException("Without change the fixed fee must consume the spending budget.");
        }
        if (collateral is { } balance && builder.CollateralReturn is null && builder.TotalCollateral.HasValue)
        {
            _ = builder.SetTotalCollateral(balance);
        }
        (PostMaryTransaction draft, ulong signingDelta) = builder.BuildDraft(requirements, minimumKeys);
        return FeeSolver.Prepare(draft, pp, inputs with
        {
            ChangeIndex = builder.ChangeOutputIndex,
            CollateralBalance = collateral,
            ReturnPresent = collateral.HasValue && builder.CollateralReturn is not null,
            ReferenceScriptSize = referenceSize,
            SigningDelta = signingDelta
        });
    }

    internal static ResolvedInput Resolve(TransactionInput input, IReadOnlyCollection<ResolvedInput> resolutions) => resolutions.FirstOrDefault(
        u => TxBuilder.InputComparer.Compare(u.Outref, input) == 0)
        ?? throw new InvalidOperationException($"Unresolved input {Convert.ToHexStringLower(input.TransactionId.Span)}#{input.Index}.");

    internal static bool TryDropChange(TransactionBuilder builder, ulong? fixedFee = null)
    {
        if (fixedFee.HasValue || builder.ChangeOutputIndex is not { } index || builder.Outputs[index].Amount() is not Lovelace)
        {
            return false;
        }
        RemoveChange(builder);
        return true;
    }

    private static void RemoveChange(TransactionBuilder builder)
    {
        if (builder.ChangeOutputIndex is { } index)
        {
            List<ITransactionOutput> outputs = [.. builder.Outputs];
            outputs.RemoveAt(index);
            _ = builder.SetOutputs(outputs);
            builder.ChangeOutputIndex = null;
            builder.ChangeOutput = null;
        }
    }

    internal static void RejectFixedFee(FeeResult fee)
    {
        if (fee.Status.HasFlag(FeeStatus.FixedFeeTooLow))
        {
            throw new InvalidOperationException($"Fixed fee is below its required fee of {fee.RequiredAtFixedFee} lovelace.");
        }
    }

    internal static InvalidOperationException InsufficientFunds(FeeResult fee) => new(fee.Status.HasFlag(FeeStatus.SpendingShort)
        ? "Insufficient spending funds." : "Insufficient collateral funds.");

    internal static void AddPlaceholders(TransactionBuilder builder, WitnessRequirements requirements, int minimumKeys)
    {
        byte[] signature = new byte[64];
        int keys = Math.Max(minimumKeys, requirements.VKeyHashes.Count);
        for (int i = 0; i < keys; i++)
        {
            byte[] key = new byte[32];
            BinaryPrimitives.WriteInt32LittleEndian(key, i);
            _ = builder.AddVKeyWitness(VKeyWitness.Create(key, signature));
        }
        for (int i = 0; i < requirements.BootstrapAttributes.Count; i++)
        {
            byte[] key = new byte[32];
            BinaryPrimitives.WriteInt32LittleEndian(key, i);
            _ = builder.AddBootstrapWitness(BootstrapWitness.Create(key, signature, new byte[32], requirements.BootstrapAttributes[i]));
        }
    }

    internal static ITransactionOutput ChangeCoin(ITransactionOutput output, ulong coin) => WithAmount(output,
        output.Amount() is LovelaceWithMultiAsset assets ? LovelaceWithMultiAsset.Create(coin, assets.MultiAsset) : Lovelace.FromAmount(coin));

    private static ulong SumCoins(IEnumerable<ResolvedInput> inputs) => inputs.Aggregate(0UL, (sum, input) => checked(sum + input.Output.Amount().Lovelace()));

    /// <summary>
    /// Evaluates Plutus scripts in the transaction and updates execution unit budgets.
    /// </summary>
    /// <exception cref="SlotConversionException">A validity bound is outside <paramref name="slotConfig"/>, or there is a bound and no <paramref name="slotConfig"/>.</exception>
    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos, SlotNetworkConfig? slotConfig)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(utxos);

        ConwayTransactionBody body = builder.BuildBody();
        PostAlonzoTransactionWitnessSet witnessSet = builder.BuildWitnessSet();

        // Source the cost model from the provider's protocol params; falls back to the
        // evaluator's baked-in latest PlutusV3 model when Pparams (or its cost models) are absent.
        IReadOnlyList<Plutus.VM.Models.EvaluationResult> evalResult =
            ScriptContextBuilder.EvaluateTx(
                body, witnessSet, utxos, slotConfig, builder.Pparams?.CostModelsForScriptLanguage);

        _ = UpdateRedeemersWithEvalResults(builder, evalResult);

        return builder;
    }

    // ──────────── Shared Helpers ────────────

    /// <summary>
    /// Computes and sets the script data hash on the builder from the current redeemers and script language.
    /// </summary>
    public static TransactionBuilder ComputeAndSetScriptDataHash(this TransactionBuilder builder, List<IScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);
        int langVersion = scripts[0].Version() - 1;
        CostMdls costMdls = builder.Pparams!.CostModelsForScriptLanguage!;
        CborList<long>? usedLanguage = langVersion switch
        {
            0 => costMdls.PlutusV1,
            1 => costMdls.PlutusV2,
            2 => costMdls.PlutusV3,
            _ => costMdls.PlutusV3
        };
        // The ledger hashes a V2 or V3 view as a fixed-length list; a decoded list (a node sends indefinite ones) would be written back as it came.
        CborList<long>? fixedLength = langVersion is 1 or 2 && usedLanguage is { } costs ? CborList.Create(costs) : null;
        CostMdls costModel = new(
            langVersion == 0 ? usedLanguage : null,
            langVersion == 1 ? fixedLength : null,
            langVersion == 2 ? fixedLength : null);
        byte[] costModelBytes = CborSerializer.Serialize(costModel);
        PostAlonzoTransactionWitnessSet ws = builder.BuildWitnessSet();
        byte[] scriptDataHash = DataHashUtil.CalculateScriptDataHash(builder.Redeemers!, ws.PlutusDataSet, costModelBytes);
        return builder.SetScriptDataHash(scriptDataHash);
    }

    /// <summary>
    /// Computes the script execution fee from the builder's current redeemers and protocol parameters.
    /// </summary>
    public static ulong ComputeScriptExecutionFee(this TransactionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return FeeEngine.ScriptFee(builder.Redeemers, builder.Pparams!);
    }

    /// <summary>Computes the auxiliary-data hash without building a transaction body.</summary>
    public static TransactionBuilder ComputeAndSetAuxDataHash(this TransactionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.BuildAuxiliaryData() is { } aux)
        {
            _ = builder.SetAuxiliaryDataHash(Wallet.Utils.HashUtil.Blake2b256(CborSerializer.Serialize(aux)));
        }
        return builder;
    }

    // ──────────── Collateral Selection ────────────

    /// <summary>
    /// Selects collateral inputs and builds a collateral return output.
    /// </summary>
    internal static bool TrySelectCollateral(TransactionBuilder builder, ulong fee, List<ResolvedInput> availableInputs)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(availableInputs);
        if (availableInputs.Count == 0)
        {
            throw new ArgumentException("Available inputs for collateral selection cannot be empty.", nameof(availableInputs));
        }

        // Prefer ADA-only UTxOs, falling back to the full pool (including token-bearing UTxOs) when needed.
        List<ResolvedInput> adaOnlyInputs = [.. availableInputs.Where(u => u.Output.Amount() is Lovelace)];
        if (adaOnlyInputs.Count > 0 && TryPickCollateral(builder, fee, adaOnlyInputs, out bool changed))
        {
            return changed;
        }
        return adaOnlyInputs.Count < availableInputs.Count
            && TryPickCollateral(builder, fee, availableInputs, out bool fallbackChanged)
            && fallbackChanged;
    }

    private static bool TryPickCollateral(
        TransactionBuilder builder, ulong fee, List<ResolvedInput> collateralCandidates, out bool changed)
    {
        changed = false;
        ulong totalCollateral = FeeEngine.RequiredCollateral(fee, builder.Pparams!.CollateralPercentage!.Value);
        ulong cost = builder.Pparams.AdaPerUTxOByte!.Value;
        int maxCollateralInputs = (int)(builder.Pparams.MaxCollateralInputs ?? 3);

        List<ResolvedInput> selected = [];
        foreach (ResolvedInput candidate in collateralCandidates
            .OrderByDescending(u => u.Output.Amount().Lovelace())
            .Take(maxCollateralInputs))
        {
            selected.Add(candidate);
            ulong totalCollateralLovelace = SumCoins(selected);
            if (totalCollateralLovelace < totalCollateral)
            {
                continue;
            }

            ulong returnLovelace = totalCollateralLovelace - totalCollateral;
            Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> aggregatedAssets = AggregateAssets(selected);

            IValue returnValue = aggregatedAssets.Count > 0
                ? LovelaceWithMultiAsset.Create(returnLovelace, MultiAssetOutput.Create(CborMap<ReadOnlyMemory<byte>, TokenBundleOutput>.Create(aggregatedAssets)))
                : Lovelace.Create(returnLovelace);

            Address returnAddress = selected[0].Output switch
            {
                AlonzoTransactionOutput alonzo => alonzo.Address,
                PostAlonzoTransactionOutput postAlonzo => postAlonzo.Address,
                _ => throw new InvalidOperationException("Invalid transaction output type")
            };

            ITransactionOutput returnOutput = new OutputBuilder(returnAddress.Value.ToArray(), returnValue).Build();
            if (returnLovelace < FeeEngine.MinLovelace(returnOutput, cost))
            {
                continue;
            }

            if (builder.CollateralReturn is not null && builder.CollateralInputs is { } original
                && original.Count == selected.Count
                && original.Zip(selected).All(pair => TxBuilder.InputComparer.Compare(pair.First, pair.Second.Outref) == 0))
            {
                return true;
            }
            _ = builder.ClearCollateral().SetTotalCollateral(totalCollateral).SetCollateralReturn(returnOutput);
            foreach (ResolvedInput input in selected)
            {
                _ = builder.AddCollateral(input.Outref);
            }
            changed = true;
            return true;
        }

        return false;
    }

    // ──────────── Change Output Building ────────────

    /// <summary>
    /// Builds a multi-asset change output from the difference between resolved inputs and current outputs.
    /// </summary>
    internal static (ulong Budget, ulong Deficit) BuildChangeOutput(
        TransactionBuilder builder, List<ResolvedInput> resolvedInputs, string changeAddress, bool includeCertificateCoins = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolvedInputs);
        ArgumentNullException.ThrowIfNull(changeAddress);
        RemoveChange(builder);

        // Compute total input value from resolved inputs
        IValue totalIn = Lovelace.Create(0);
        foreach (ResolvedInput utxo in resolvedInputs)
        {
            totalIn = totalIn.Merge(utxo.Output.Amount());
        }

        // Compute total output value (excluding change)
        IValue totalOut = Lovelace.Create(0);
        for (int i = 0; i < builder.Outputs.Count; i++)
        {
            totalOut = totalOut.Merge(builder.Outputs[i].Amount());
        }

        // Account for implicit inputs (withdrawals, refunds) and implicit outputs (deposits)
        ulong deposits = 0;
        ulong refunds = 0;
        if (includeCertificateCoins && builder.CertificatesList is not null)
        {
            ulong keyDep = builder.Pparams?.KeyDeposit ?? 2_000_000;
            ulong poolDep = builder.Pparams?.PoolDeposit ?? 500_000_000;
            foreach (ICertificate cert in builder.CertificatesList)
            {
                deposits += cert.GetDeposit(keyDep, poolDep);
                refunds += cert.GetRefund(keyDep, poolDep);
            }
        }

        ulong totalWithdrawals = 0;
        if (builder.CurrentWithdrawals is { } withdrawals)
        {
            foreach (KeyValuePair<RewardAccount, ulong> kvp in withdrawals.Value)
            {
                totalWithdrawals += kvp.Value;
            }
        }

        // Account for minted/burned tokens in value conservation:
        // inputs + mint + withdrawals + refunds = outputs + fee + deposits
        IValue mintValue = Lovelace.Create(0);
        if (builder.Mint is not null)
        {
            Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> positiveAssets = [with(ReadOnlyMemoryComparer.Instance)];
            Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> negativeAssets = [with(ReadOnlyMemoryComparer.Instance)];

            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> policy in builder.Mint.Value.Value)
            {
                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> asset in policy.Value.Value)
                {
                    if (asset.Value > 0)
                    {
                        if (!positiveAssets.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                        {
                            bundle = [with(ReadOnlyMemoryComparer.Instance)];
                            positiveAssets[policy.Key] = bundle;
                        }
                        bundle[asset.Key] = (ulong)asset.Value;
                    }
                    else if (asset.Value < 0)
                    {
                        if (!negativeAssets.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                        {
                            bundle = [with(ReadOnlyMemoryComparer.Instance)];
                            negativeAssets[policy.Key] = bundle;
                        }
                        bundle[asset.Key] = (ulong)-asset.Value;
                    }
                }
            }

            if (positiveAssets.Count > 0)
            {
                Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> posMulti = [with(ReadOnlyMemoryComparer.Instance)];
                foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> kv in positiveAssets)
                {
                    posMulti[kv.Key] = TokenBundleOutput.Create(CborMap<ReadOnlyMemory<byte>, ulong>.Create(kv.Value));
                }
                mintValue = mintValue.Merge(LovelaceWithMultiAsset.Create(0, MultiAssetOutput.Create(CborMap<ReadOnlyMemory<byte>, TokenBundleOutput>.Create(posMulti))));
            }

            if (negativeAssets.Count > 0)
            {
                Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> negMulti = [with(ReadOnlyMemoryComparer.Instance)];
                foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> kv in negativeAssets)
                {
                    negMulti[kv.Key] = TokenBundleOutput.Create(CborMap<ReadOnlyMemory<byte>, ulong>.Create(kv.Value));
                }
                totalOut = totalOut.Merge(LovelaceWithMultiAsset.Create(0, MultiAssetOutput.Create(CborMap<ReadOnlyMemory<byte>, TokenBundleOutput>.Create(negMulti))));
            }
        }

        IValue available = totalIn.Merge(mintValue).Merge(Lovelace.Create(checked(totalWithdrawals + refunds)));
        IValue required = totalOut.Merge(Lovelace.Create(deposits));
        IValue change = available.Subtract(required);
        if (change.Lovelace() != 0 || change is LovelaceWithMultiAsset)
        {
            ITransactionOutput output = new OutputBuilder(changeAddress, change).Build();
            _ = builder.AddOutputInternal(output, isChange: true);
        }
        return (change.Lovelace(), required.Lovelace() > available.Lovelace() ? required.Lovelace() - available.Lovelace() : 0);
    }

    // ──────────── Redeemer Update ────────────

    internal static IEnumerable<(int Tag, ulong Index, ulong Mem, ulong Steps)> RedeemerUnits(IRedeemers? redeemers) => redeemers switch
    {
        RedeemerMap map => map.Value.Select(entry => (entry.Key.Tag, entry.Key.Index, entry.Value.ExUnits.Mem, entry.Value.ExUnits.Steps)),
        RedeemerList list => list.Value.Select(entry => (entry.Tag, entry.Index, entry.ExUnits.Mem, entry.ExUnits.Steps)),
        _ => []
    };

    internal static bool UpdateRedeemersWithEvalResults(
        TransactionBuilder builder, IReadOnlyList<Plutus.VM.Models.EvaluationResult> evalResult)
    {
        Dictionary<(int, ulong), ExUnits> evaluated = [];
        foreach (Plutus.VM.Models.EvaluationResult result in evalResult)
        {
            if (!evaluated.TryAdd((result.RedeemerTag, result.Index), ExUnits.Create(result.ExUnits.Mem, result.ExUnits.Steps)))
            {
                throw new InvalidOperationException($"Duplicate evaluated redeemer ({result.RedeemerTag}, {result.Index}).");
            }
        }
        bool changed = false;
        IRedeemers? updated = builder.Redeemers switch
        {
            RedeemerList list => RedeemerList.Create([.. list.Value.Select(entry => RedeemerEntry.Create(entry.Tag, entry.Index, entry.Data,
                Take(entry.Tag, entry.Index, entry.ExUnits)))]),
            RedeemerMap map => RedeemerMap.Create(CborMap<RedeemerKey, RedeemerValue>.Create(map.Value.Select(entry => KeyValuePair.Create(
                entry.Key, RedeemerValue.Create(entry.Value.Data, Take(entry.Key.Tag, entry.Key.Index, entry.Value.ExUnits)))))),
            _ => null
        };
        if (evaluated.Count != 0)
        {
            throw new InvalidOperationException($"Unknown evaluated redeemer {evaluated.Keys.First()}.");
        }
        if (changed && updated is not null)
        {
            _ = builder.SetRedeemers(updated);
        }
        return changed;

        ExUnits Take(int tag, ulong index, ExUnits old)
        {
            if (!evaluated.Remove((tag, index), out ExUnits units))
            {
                throw new InvalidOperationException($"Missing evaluated redeemer ({tag}, {index}).");
            }
            changed |= units.Mem != old.Mem || units.Steps != old.Steps;
            return units;
        }
    }

    // ──────────── Helpers ────────────

    private static Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> AggregateAssets(
        List<ResolvedInput> inputs)
    {
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> result = [with(ReadOnlyMemoryComparer.Instance)];

        foreach (ResolvedInput input in inputs)
        {
            if (input.Output.Amount() is not LovelaceWithMultiAsset multiAsset)
            {
                continue;
            }

            foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput tokenBundle) in multiAsset.MultiAsset.Value)
            {
                if (!result.TryGetValue(policyId, out TokenBundleOutput existingBundle))
                {
                    result[policyId] = tokenBundle;
                    continue;
                }

                Dictionary<ReadOnlyMemory<byte>, ulong> merged = [with(ReadOnlyMemoryComparer.Instance)];
                foreach ((ReadOnlyMemory<byte> name, ulong amount) in existingBundle.Value)
                {
                    merged[name] = amount;
                }

                foreach ((ReadOnlyMemory<byte> name, ulong amount) in tokenBundle.Value)
                {
                    merged[name] = merged.TryGetValue(name, out ulong existing) ? existing + amount : amount;
                }

                result[policyId] = TokenBundleOutput.Create(CborMap<ReadOnlyMemory<byte>, ulong>.Create(merged));
            }
        }

        return result;
    }

    private static ITransactionOutput WithAmount(ITransactionOutput output, IValue newValue) => output switch
    {
        AlonzoTransactionOutput alonzo => AlonzoTransactionOutput.Create(
            alonzo.Address, newValue, alonzo.DatumHash),
        PostAlonzoTransactionOutput postAlonzo => PostAlonzoTransactionOutput.Create(
            postAlonzo.Address, newValue, postAlonzo.Datum, postAlonzo.ScriptRef),
        _ => throw new InvalidOperationException($"Unsupported transaction output type: {output.GetType().Name}")
    };
}
