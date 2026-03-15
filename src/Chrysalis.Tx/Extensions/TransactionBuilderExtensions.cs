using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
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
        List<ResolvedInput>? availableInputs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);

        builder.IntegrateRedeemerSet();

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;

        if (builder.Redeemers is not null)
        {
            (scriptFee, scriptExecutionFee) = CalculateScriptFees(builder, scripts);
        }

        _ = builder.SetFee(defaultFee == 0 ? InitialFeePlaceholder : defaultFee);

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

        AdjustChangeOutput(builder, fee);

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

    // ──────────── Script Fee Calculation ────────────

    private static (ulong ScriptFee, ulong ExecutionFee) CalculateScriptFees(
        TransactionBuilder builder, List<IScript> scripts)
    {
        if (scripts.Count <= 0)
        {
            throw new ArgumentException("Missing script", nameof(scripts));
        }

        // Compute script data hash
        int langVersion = scripts[0].Version() - 1;
        CostMdls costMdls = builder.Pparams!.CostModelsForScriptLanguage!;
        ICborMaybeIndefList<long>? usedLanguage = langVersion switch
        {
            0 => costMdls.PlutusV1,
            1 => costMdls.PlutusV2,
            2 => costMdls.PlutusV3,
            _ => throw new ArgumentException($"Unsupported script language version: {langVersion}", nameof(scripts))
        };

        CostMdls costModel = new(
            langVersion == 0 ? usedLanguage : null,
            langVersion == 1 ? usedLanguage : null,
            langVersion == 2 ? usedLanguage : null
        );

        byte[] costModelBytes = CborSerializer.Serialize(costModel);
        PostAlonzoTransactionWitnessSet witnessSet = builder.BuildWitnessSet();
        byte[] scriptDataHash = DataHashUtil.CalculateScriptDataHash(builder.Redeemers!, witnessSet.PlutusDataSet, costModelBytes);
        _ = builder.SetScriptDataHash(scriptDataHash);

        // Reference script fee (tiered pricing on total script size)
        ulong scriptCostPerByte = builder.Pparams!.MinFeeRefScriptCostPerByte!.Numerator
            / builder.Pparams.MinFeeRefScriptCostPerByte!.Denominator;
        int totalScriptSize = scripts.Sum(script => script.Bytes().Length);
        ulong scriptFee = FeeUtil.CalculateReferenceScriptFee(totalScriptSize, scriptCostPerByte);

        // Execution fee from redeemers
        RationalNumber memUnitsCost = new(
            builder.Pparams!.ExecutionCosts!.Value.MemPrice.Numerator,
            builder.Pparams.ExecutionCosts!.Value.MemPrice.Denominator);
        RationalNumber stepUnitsCost = new(
            builder.Pparams.ExecutionCosts!.Value.StepPrice.Numerator,
            builder.Pparams.ExecutionCosts!.Value.StepPrice.Denominator);
        ulong executionFee = FeeUtil.CalculateScriptExecutionFee(builder.Redeemers!, stepUnitsCost, memUnitsCost);

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

    private static void SelectCollateral(
        TransactionBuilder builder, ulong fee, List<ResolvedInput> availableInputs)
    {
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

        // Select collateral UTxOs
        CoinSelectionResult collateralSelection;
        try
        {
            collateralSelection = CoinSelectionUtil.Select(
                availableInputs,
                [Lovelace.Create(totalCollateralNeeded)],
                CoinSelectionStrategy.LargestFirst,
                maxCollateralInputs);
        }
        catch (InvalidOperationException)
        {
            collateralSelection = CoinSelectionUtil.Select(
                availableInputs,
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

    // ──────────── Change Output Adjustment ────────────

    private static void AdjustChangeOutput(TransactionBuilder builder, ulong fee)
    {
        if (builder.ChangeOutput is null)
        {
            return;
        }

        List<ITransactionOutput> outputs = [.. builder.Outputs];

        ulong currentChangeLovelace = builder.ChangeOutput.Amount().Lovelace();
        ulong updatedChangeLovelace = currentChangeLovelace > fee ? currentChangeLovelace - fee : 0;

        IValue changeOutputValue = builder.ChangeOutput.Amount();
        IValue updatedValue = changeOutputValue is LovelaceWithMultiAsset lma
            ? LovelaceWithMultiAsset.Create(updatedChangeLovelace, lma.MultiAsset)
            : Lovelace.Create(updatedChangeLovelace);

        // Remove last output (assumed to be change)
        int changeIndex = builder.ChangeOutputIndex ?? (outputs.Count - 1);
        if (changeIndex >= 0 && changeIndex < outputs.Count)
        {
            outputs.RemoveAt(changeIndex);
        }

        if (updatedChangeLovelace > 0)
        {
            ITransactionOutput updatedChange = RebuildOutput(builder.ChangeOutput, updatedValue);

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
            _ = builder.SetCollateralReturn(RebuildOutput(builder.CollateralReturn, newReturnValue));
        }
    }

    // ──────────── Redeemer Update ────────────

    private static void UpdateRedeemersWithEvalResults(
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

    private static ITransactionOutput RebuildOutput(ITransactionOutput output, IValue newValue) => output switch
    {
        AlonzoTransactionOutput alonzo => AlonzoTransactionOutput.Create(
            alonzo.Address, newValue, alonzo.DatumHash),
        PostAlonzoTransactionOutput postAlonzo => PostAlonzoTransactionOutput.Create(
            postAlonzo.Address, newValue, postAlonzo.Datum, postAlonzo.ScriptRef),
        _ => throw new InvalidOperationException("Unsupported transaction output type")
    };
}
