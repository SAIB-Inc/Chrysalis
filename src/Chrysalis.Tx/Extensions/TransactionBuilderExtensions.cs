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
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for fee calculation and script evaluation on the transaction builder.
/// </summary>
public static class TransactionBuilderExtensions
{
    private const int MaxFeeIterations = 10;
    private const long FeeConvergenceTolerance = 1000;
    private const ulong InitialFeePlaceholder = 2_000_000;

    /// <summary>
    /// Calculates and sets the transaction fee, including script fees and collateral handling.
    /// </summary>
    public static TransactionBuilder CalculateFee(
        this TransactionBuilder builder,
        List<IScript> scripts,
        ulong defaultFee = 0,
        int mockWitnessFee = 1,
        List<ResolvedInput>? availableInputs = null,
        string? changeAddress = null,
        List<ResolvedInput>? resolvedInputs = null)
    {
        // Convert to sized tuples using serialized length as size estimate
        List<(IScript Script, int RawSize)> sized = [.. scripts
            .Select(s => (s, CborSerializer.Serialize(s).Length))];
        return builder.CalculateFee(sized, defaultFee, mockWitnessFee, availableInputs, changeAddress, resolvedInputs);
    }

    /// <summary>
    /// Calculates and sets the transaction fee with exact reference script sizes.
    /// </summary>
    internal static TransactionBuilder CalculateFee(
        this TransactionBuilder builder,
        List<(IScript Script, int RawSize)> scripts,
        ulong defaultFee = 0,
        int mockWitnessFee = 1,
        List<ResolvedInput>? availableInputs = null,
        string? changeAddress = null,
        List<ResolvedInput>? resolvedInputs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);

        builder.IntegrateRedeemerSet();
        _ = builder.ComputeAndSetAuxDataHash();

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;

        if (builder.Redeemers is not null)
        {
            (scriptFee, scriptExecutionFee) = CalculateScriptFees(builder, scripts);
        }

        _ = builder.SetFee(defaultFee == 0 ? InitialFeePlaceholder : defaultFee);

        // Build change output before convergence (so fee accounts for its CBOR size)
        if (changeAddress is not null && resolvedInputs is not null)
        {
            BuildChangeOutput(builder, resolvedInputs, changeAddress);
        }

        ulong fee = ConvergeFee(builder, scriptFee, scriptExecutionFee, mockWitnessFee, availableInputs);

        if (defaultFee > 0)
        {
            fee = defaultFee;
        }

        ulong previousFee = builder.Fee;
        _ = builder.SetFee(fee);

        // Sync totalCollateral with the final fee — the convergence loop may have
        // exited before SelectCollateral ran with the last fee value.
        if (builder.Redeemers is not null && fee != previousFee)
        {
            AdjustCollateralForFeeIncrease(builder, previousFee, fee);
        }

        // Rebuild change with final fee
        if (changeAddress is not null && resolvedInputs is not null)
        {
            BuildChangeOutput(builder, resolvedInputs, changeAddress);
        }
        else
        {
            AdjustChangeOutput(builder, fee);
        }

        return builder;
    }

    /// <summary>
    /// Evaluates Plutus scripts in the transaction and updates execution unit budgets.
    /// </summary>
    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos, SlotNetworkConfig slotConfig)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(utxos);
        ArgumentNullException.ThrowIfNull(slotConfig);

        ConwayTransactionBody body = builder.BuildBody();
        PostAlonzoTransactionWitnessSet witnessSet = builder.BuildWitnessSet();

        IReadOnlyList<Plutus.VM.Models.EvaluationResult> evalResult =
            ScriptContextBuilder.EvaluateTx(body, witnessSet, utxos, slotConfig);

        UpdateRedeemersWithEvalResults(builder, evalResult);

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
        ICborMaybeIndefList<long>? usedLanguage = langVersion switch
        {
            0 => costMdls.PlutusV1,
            1 => costMdls.PlutusV2,
            2 => costMdls.PlutusV3,
            _ => costMdls.PlutusV3
        };
        CostMdls costModel = new(
            langVersion == 0 ? usedLanguage : null,
            langVersion == 1 ? usedLanguage : null,
            langVersion == 2 ? usedLanguage : null);
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
        if (builder.Redeemers is null)
        {
            return 0;
        }

        RationalNumber memPrice = new(
            builder.Pparams!.ExecutionCosts!.Value.MemPrice.Numerator,
            builder.Pparams.ExecutionCosts.Value.MemPrice.Denominator);
        RationalNumber stepPrice = new(
            builder.Pparams.ExecutionCosts!.Value.StepPrice.Numerator,
            builder.Pparams.ExecutionCosts.Value.StepPrice.Denominator);
        return FeeUtil.CalculateScriptExecutionFee(builder.Redeemers, stepPrice, memPrice);
    }

    /// <summary>
    /// Computes and sets the auxiliary data hash from the builder's current metadata.
    /// </summary>
    public static TransactionBuilder ComputeAndSetAuxDataHash(this TransactionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PostMaryTransaction tx = builder.Build();
        if (tx.AuxiliaryData is not null)
        {
            byte[] auxBytes = CborSerializer.Serialize(tx.AuxiliaryData);
            _ = builder.SetAuxiliaryDataHash(Wallet.Utils.HashUtil.Blake2b256(auxBytes));
        }
        return builder;
    }

    // ──────────── Script Fee Calculation ────────────

    private static (ulong ScriptFee, ulong ExecutionFee) CalculateScriptFees(
        TransactionBuilder builder, List<(IScript Script, int RawSize)> scripts)
    {
        if (scripts.Count <= 0)
        {
            throw new ArgumentException("Missing script", nameof(scripts));
        }

        List<IScript> scriptList = [.. scripts.Select(s => s.Script)];
        _ = builder.ComputeAndSetScriptDataHash(scriptList);

        // Reference script fee (tiered pricing on full CBOR-encoded ScriptRef size)
        ulong scriptCostPerByte = builder.Pparams!.MinFeeRefScriptCostPerByte!.Numerator
            / builder.Pparams.MinFeeRefScriptCostPerByte!.Denominator;
        int totalScriptSize = scripts.Sum(s => s.RawSize);
        ulong scriptFee = FeeUtil.CalculateReferenceScriptFee(totalScriptSize, scriptCostPerByte);

        ulong executionFee = builder.ComputeScriptExecutionFee();

        return (scriptFee, executionFee);
    }

    // ──────────── Fee Convergence Loop ────────────

    private static ulong ConvergeFee(
        TransactionBuilder builder,
        ulong scriptFee,
        ulong scriptExecutionFee,
        int mockWitnessFee,
        List<ResolvedInput>? availableInputs)
    {
        ulong previousFee = 0;
        ulong fee = 0;

        // Set initial collateral placeholder if scripts are involved
        if (builder.Redeemers is not null)
        {
            _ = builder.SetTotalCollateral(InitialFeePlaceholder);
        }

        for (int iteration = 0; iteration < MaxFeeIterations; iteration++)
        {
            ITransaction draftTx = builder.Build();
            byte[] draftTxCborBytes = CborSerializer.Serialize(draftTx);

            fee = FeeUtil.CalculateFeeWithWitness(
                (ulong)draftTxCborBytes.Length,
                builder.Pparams!.MinFeeA!.Value,
                builder.Pparams.MinFeeB!.Value,
                mockWitnessFee) + scriptFee + scriptExecutionFee;

            if (Math.Abs((long)fee - (long)previousFee) < FeeConvergenceTolerance)
            {
                break;
            }

            previousFee = fee;
            _ = builder.SetFee(fee);

            if (builder.Redeemers is not null && availableInputs is { Count: > 0 })
            {
                SelectCollateral(builder, fee, availableInputs);
            }

            if (iteration == MaxFeeIterations - 1)
            {
                throw new InvalidOperationException("Fee calculation did not converge after maximum iterations.");
            }
        }

        return fee;
    }

    // ──────────── Collateral Selection ────────────

    /// <summary>
    /// Selects collateral inputs and builds a collateral return output.
    /// </summary>
    internal static void SelectCollateral(
        TransactionBuilder builder, ulong fee, List<ResolvedInput> availableInputs)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(availableInputs);
        if (availableInputs.Count == 0)
        {
            throw new ArgumentException("Available inputs for collateral selection cannot be empty.", nameof(availableInputs));
        }
        ulong totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.Pparams!.CollateralPercentage!.Value);
        _ = builder.SetTotalCollateral(totalCollateral);
        _ = builder.ClearCollateral();
        _ = builder.ClearCollateralReturn();

        // Estimate min ADA for return output
        ResolvedInput firstInput = availableInputs[0];
        byte[] dummyReturnBytes = CborSerializer.Serialize(firstInput.Output);
        ulong estimatedMinReturn = FeeUtil.CalculateMinimumLovelace(
            (ulong)builder.Pparams!.AdaPerUTxOByte!, dummyReturnBytes);

        ulong totalCollateralNeeded = totalCollateral + estimatedMinReturn + (estimatedMinReturn / 2);
        int maxCollateralInputs = (int)(builder.Pparams!.MaxCollateralInputs ?? 3);

        // Prefer ADA-only UTxOs for collateral (Cardano rejects collateral with native assets)
        List<ResolvedInput> adaOnlyInputs = [.. availableInputs.Where(
            u => u.Output.Amount() is Lovelace)];
        List<ResolvedInput> collateralCandidates = adaOnlyInputs.Count > 0 ? adaOnlyInputs : availableInputs;

        // Select collateral UTxOs
        CoinSelectionResult collateralSelection;
        try
        {
            collateralSelection = CoinSelectionUtil.Select(
                collateralCandidates,
                [Lovelace.Create(totalCollateralNeeded)],
                CoinSelectionStrategy.LargestFirst,
                maxCollateralInputs);
        }
        catch (InvalidOperationException)
        {
            collateralSelection = CoinSelectionUtil.Select(
                collateralCandidates,
                [Lovelace.Create(totalCollateral)],
                CoinSelectionStrategy.LargestFirst,
                maxCollateralInputs);
        }

        foreach (ResolvedInput input in collateralSelection.Inputs)
        {
            _ = builder.AddCollateral(input.Outref);
        }

        // Build collateral return
        ulong totalCollateralLovelace = 0;
        foreach (ResolvedInput input in collateralSelection.Inputs)
        {
            totalCollateralLovelace += input.Output.Amount().Lovelace();
        }

        if (totalCollateralLovelace < totalCollateral)
        {
            throw new InvalidOperationException(
                $"Selected collateral inputs insufficient. Need {totalCollateral} but have {totalCollateralLovelace}.");
        }

        ulong returnLovelace = totalCollateralLovelace - totalCollateral;
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> aggregatedAssets = AggregateAssets(collateralSelection.Inputs);

        IValue returnValue = aggregatedAssets.Count > 0
            ? LovelaceWithMultiAsset.Create(returnLovelace, MultiAssetOutput.Create(aggregatedAssets))
            : Lovelace.Create(returnLovelace);

        Address returnAddress = collateralSelection.Inputs[0].Output switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Address,
            PostAlonzoTransactionOutput postAlonzo => postAlonzo.Address,
            _ => throw new InvalidOperationException("Invalid transaction output type")
        };

        ITransactionOutput returnOutput = PostAlonzoTransactionOutput.Create(returnAddress, returnValue, null, null);

        byte[] returnOutputBytes = CborSerializer.Serialize(returnOutput);
        ulong minReturn = FeeUtil.CalculateMinimumLovelace(
            (ulong)builder.Pparams!.AdaPerUTxOByte!, returnOutputBytes);

        if (returnLovelace < minReturn)
        {
            throw new InvalidOperationException(
                $"Collateral return ({returnLovelace}) below minimum ADA ({minReturn}).");
        }

        _ = builder.SetCollateralReturn(returnOutput);
    }

    // ──────────── Change Output Building ────────────

    /// <summary>
    /// Builds a multi-asset change output from the difference between resolved inputs and current outputs.
    /// </summary>
    internal static void BuildChangeOutput(
        TransactionBuilder builder, List<ResolvedInput> resolvedInputs, string changeAddress)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolvedInputs);
        ArgumentNullException.ThrowIfNull(changeAddress);
        // Remove existing change output
        if (builder.ChangeOutputIndex is not null)
        {
            List<ITransactionOutput> outputs = [.. builder.Outputs];
            outputs.RemoveAt(builder.ChangeOutputIndex.Value);
            _ = builder.SetOutputs(outputs);
            builder.ChangeOutputIndex = null;
            builder.ChangeOutput = null;
        }

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
        if (builder.CertificatesList is not null)
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
        if (builder.CurrentWithdrawals is not null)
        {
            foreach (KeyValuePair<RewardAccount, ulong> kvp in builder.CurrentWithdrawals.Value)
            {
                totalWithdrawals += kvp.Value;
            }
        }

        // Account for minted/burned tokens in value conservation:
        // inputs + mint + withdrawals + refunds = outputs + fee + deposits
        IValue mintValue = Lovelace.Create(0);
        if (builder.Mint is not null)
        {
            Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> positiveAssets = new(ReadOnlyMemoryComparer.Instance);
            Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> negativeAssets = new(ReadOnlyMemoryComparer.Instance);

            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleMint> policy in builder.Mint.Value.Value)
            {
                foreach (KeyValuePair<ReadOnlyMemory<byte>, long> asset in policy.Value.Value)
                {
                    if (asset.Value > 0)
                    {
                        if (!positiveAssets.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                        {
                            bundle = new(ReadOnlyMemoryComparer.Instance);
                            positiveAssets[policy.Key] = bundle;
                        }
                        bundle[asset.Key] = (ulong)asset.Value;
                    }
                    else if (asset.Value < 0)
                    {
                        if (!negativeAssets.TryGetValue(policy.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                        {
                            bundle = new(ReadOnlyMemoryComparer.Instance);
                            negativeAssets[policy.Key] = bundle;
                        }
                        bundle[asset.Key] = (ulong)-asset.Value;
                    }
                }
            }

            if (positiveAssets.Count > 0)
            {
                Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> posMulti = new(ReadOnlyMemoryComparer.Instance);
                foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> kv in positiveAssets)
                {
                    posMulti[kv.Key] = TokenBundleOutput.Create(kv.Value);
                }
                mintValue = mintValue.Merge(LovelaceWithMultiAsset.Create(0, MultiAssetOutput.Create(posMulti)));
            }

            if (negativeAssets.Count > 0)
            {
                Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> negMulti = new(ReadOnlyMemoryComparer.Instance);
                foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> kv in negativeAssets)
                {
                    negMulti[kv.Key] = TokenBundleOutput.Create(kv.Value);
                }
                totalOut = totalOut.Merge(LovelaceWithMultiAsset.Create(0, MultiAssetOutput.Create(negMulti)));
            }
        }

        IValue change = totalIn
            .Merge(mintValue)
            .Merge(Lovelace.Create(totalWithdrawals + refunds))
            .Subtract(totalOut)
            .Subtract(Lovelace.Create(builder.Fee + deposits));
        if (change.Lovelace() > 0)
        {
            _ = builder.AddOutput(changeAddress, change, isChange: true);
        }
    }

    // ──────────── Change Output Adjustment ────────────

    private static void AdjustChangeOutput(TransactionBuilder builder, ulong fee)
    {
        if (builder.ChangeOutput is null || builder.ChangeOutputIndex is null)
        {
            return;
        }

        List<ITransactionOutput> outputs = [.. builder.Outputs];
        int changeIndex = builder.ChangeOutputIndex.Value;
        if (changeIndex < 0 || changeIndex >= outputs.Count)
        {
            return;
        }

        ulong currentChangeLovelace = builder.ChangeOutput.Amount().Lovelace();
        ulong updatedChangeLovelace = currentChangeLovelace > fee ? currentChangeLovelace - fee : 0;

        IValue changeOutputValue = builder.ChangeOutput.Amount();
        IValue updatedValue = changeOutputValue is LovelaceWithMultiAsset lma
            ? LovelaceWithMultiAsset.Create(updatedChangeLovelace, lma.MultiAsset)
            : Lovelace.Create(updatedChangeLovelace);

        outputs.RemoveAt(changeIndex);

        if (updatedChangeLovelace > 0)
        {
            ITransactionOutput updatedChange = WithAmount(builder.ChangeOutput, updatedValue);

            byte[] updatedBytes = CborSerializer.Serialize(updatedChange);
            ulong minLovelace = FeeUtil.CalculateMinimumLovelace(
                (ulong)builder.Pparams!.AdaPerUTxOByte!, updatedBytes);

            if (updatedChangeLovelace < minLovelace)
            {
                // Change too small — absorb into fee
                ulong adjustedFee = fee + updatedChangeLovelace;
                _ = builder.SetFee(adjustedFee);
                AdjustCollateralForFeeIncrease(builder, fee, adjustedFee);
            }
            else
            {
                outputs.Add(updatedChange);
            }
        }

        _ = builder.SetOutputs(outputs);
    }

    // ──────────── Collateral Fee Adjustment ────────────

    private static void AdjustCollateralForFeeIncrease(TransactionBuilder builder, ulong oldFee, ulong newFee)
    {
        if (builder.TotalCollateral is null || builder.Pparams?.CollateralPercentage is null)
        {
            return;
        }

        ulong newTotalCollateral = FeeUtil.CalculateRequiredCollateral(newFee, builder.Pparams.CollateralPercentage.Value);
        ulong oldTotalCollateral = FeeUtil.CalculateRequiredCollateral(oldFee, builder.Pparams.CollateralPercentage.Value);
        _ = builder.SetTotalCollateral(newTotalCollateral);

        if (newTotalCollateral > oldTotalCollateral && builder.CollateralReturn is not null)
        {
            ulong increase = newTotalCollateral - oldTotalCollateral;
            ulong currentReturn = builder.CollateralReturn.Amount().Lovelace();

            if (currentReturn <= increase)
            {
                throw new InvalidOperationException(
                    $"Insufficient collateral: return ({currentReturn}) cannot cover additional {increase} needed.");
            }

            IValue newReturnValue = builder.CollateralReturn.Amount().WithLovelace(currentReturn - increase);
            _ = builder.SetCollateralReturn(WithAmount(builder.CollateralReturn, newReturnValue));
        }
    }

    // ──────────── Redeemer Update ────────────

    internal static void UpdateRedeemersWithEvalResults(
        TransactionBuilder builder,
        IReadOnlyList<Plutus.VM.Models.EvaluationResult> evalResult)
    {
        switch (builder.Redeemers)
        {
            case RedeemerList redeemersList:
                List<RedeemerEntry> updated = [];
                foreach (RedeemerEntry redeemer in redeemersList.Value)
                {
                    ExUnits exUnits = FindExUnits(evalResult, redeemer.Tag, redeemer.Index) ?? redeemer.ExUnits;
                    updated.Add(RedeemerEntry.Create(redeemer.Tag, redeemer.Index, redeemer.Data, exUnits));
                }

                _ = builder.SetRedeemers(RedeemerList.Create(updated));
                break;

            case RedeemerMap redeemersMap:
                Dictionary<RedeemerKey, RedeemerValue> updatedMap = [];
                foreach (KeyValuePair<RedeemerKey, RedeemerValue> kvp in redeemersMap.Value)
                {
                    ExUnits exUnits = FindExUnits(evalResult, kvp.Key.Tag, kvp.Key.Index) ?? kvp.Value.ExUnits;
                    updatedMap.Add(RedeemerKey.Create(kvp.Key.Tag, kvp.Key.Index), RedeemerValue.Create(kvp.Value.Data, exUnits));
                }

                _ = builder.SetRedeemers(RedeemerMap.Create(updatedMap));
                break;

            default:
                break;
        }
    }

    private static ExUnits? FindExUnits(
        IReadOnlyList<Plutus.VM.Models.EvaluationResult> results, int tag, ulong index)
    {
        foreach (Plutus.VM.Models.EvaluationResult r in results)
        {
            if (r.RedeemerTag == tag && r.Index == index)
            {
                return ExUnits.Create(r.ExUnits.Mem, r.ExUnits.Steps);
            }
        }

        return null;
    }

    // ──────────── Helpers ────────────

    private static Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> AggregateAssets(
        List<ResolvedInput> inputs)
    {
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> result = new(ReadOnlyMemoryComparer.Instance);

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

                Dictionary<ReadOnlyMemory<byte>, ulong> merged = new(ReadOnlyMemoryComparer.Instance);
                foreach ((ReadOnlyMemory<byte> name, ulong amount) in existingBundle.Value)
                {
                    merged[name] = amount;
                }

                foreach ((ReadOnlyMemory<byte> name, ulong amount) in tokenBundle.Value)
                {
                    merged[name] = merged.TryGetValue(name, out ulong existing) ? existing + amount : amount;
                }

                result[policyId] = TokenBundleOutput.Create(merged);
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
