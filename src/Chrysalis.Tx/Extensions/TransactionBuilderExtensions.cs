using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for fee calculation and script evaluation on the transaction builder.
/// </summary>
public static class TransactionBuilderExtensions
{
    /// <summary>
    /// Calculates and sets the transaction fee, including script fees and collateral handling.
    /// </summary>
    /// <param name="builder">The transaction builder.</param>
    /// <param name="scripts">The scripts used in the transaction.</param>
    /// <param name="defaultFee">An optional fixed fee override.</param>
    /// <param name="mockWitnessFee">Number of mock witnesses for fee estimation.</param>
    /// <param name="availableInputs">Available inputs for collateral selection.</param>
    /// <returns>The transaction builder with fee set.</returns>
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, List<Script> scripts, ulong defaultFee = 0, int mockWitnessFee = 1, List<ResolvedInput>? availableInputs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;
        if (builder.WitnessSet.Redeemers is not null)
        {
            _ = builder.SetTotalCollateral(2000000UL);

            if (scripts.Count <= 0)
            {
                throw new ArgumentException("Missing script", nameof(scripts));
            }

            CborMaybeIndefList<long> usedLanguage = builder.Pparams!.CostModelsForScriptLanguage!.Value[scripts[0].Version() - 1];
            CostMdls costModel = new(new Dictionary<int, CborMaybeIndefList<long>>(){
                 { scripts[0].Version() - 1, usedLanguage }
            });
            byte[] costModelBytes = CborSerializer.Serialize(costModel);
            byte[] scriptDataHash = DataHashUtil.CalculateScriptDataHash(builder.WitnessSet.Redeemers, builder.WitnessSet.PlutusDataSet?.GetValue() as PlutusList, costModelBytes);
            _ = builder.SetScriptDataHash(scriptDataHash);

            ulong scriptCostPerByte = builder.Pparams!.MinFeeRefScriptCostPerByte!.Numerator / builder.Pparams.MinFeeRefScriptCostPerByte!.Denominator;

            // Tiered pricing applies to TOTAL script size, not per-script
            int totalScriptSize = scripts.Sum(script => script.Bytes().Length);
            scriptFee = FeeUtil.CalculateReferenceScriptFee(totalScriptSize, scriptCostPerByte);

            RationalNumber memUnitsCost = new(builder.Pparams!.ExecutionCosts!.MemPrice!.Numerator!, builder.Pparams.ExecutionCosts!.MemPrice!.Denominator!);
            RationalNumber stepUnitsCost = new(builder.Pparams.ExecutionCosts!.StepPrice!.Numerator!, builder.Pparams.ExecutionCosts!.StepPrice!.Denominator!);
            scriptExecutionFee = FeeUtil.CalculateScriptExecutionFee(builder.WitnessSet.Redeemers, memUnitsCost, stepUnitsCost);

        }

        _ = builder.SetFee(defaultFee == 0 ? 2000000UL : defaultFee);

        // Recursive fee and collateral calculation
        ulong previousFee = 0;
        ulong fee = 0;
        int iterations = 0;
        int maxIterations = 10;

        while (iterations < maxIterations)
        {
            // Calculate current fee based on current transaction state
            Transaction draftTx = builder.Build();
            byte[] draftTxCborBytes = CborSerializer.Serialize(draftTx);
            ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;

            fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.Pparams!.MinFeeA!.Value, builder!.Pparams.MinFeeB!.Value, mockWitnessFee) + scriptFee + scriptExecutionFee;

            // If fee hasn't changed significantly, we're done
            if (Math.Abs((long)fee - (long)previousFee) < 1000) // 1000 lovelace tolerance
            {
                break;
            }

            previousFee = fee;
            _ = builder.SetFee(fee);

            // Handle collateral if needed
            if (builder.Body.TotalCollateral is not null && availableInputs != null && availableInputs.Count > 0)
            {
                ulong totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.Pparams!.CollateralPercentage!.Value);
                _ = builder.SetTotalCollateral(totalCollateral);

                // Clear any existing collateral settings to start fresh
                builder.Body = builder.Body with { Collateral = null, CollateralReturn = null };

                // Estimate minimum ADA needed for return output
                ResolvedInput firstInput = availableInputs[0];
                TransactionOutput dummyReturnOutput = firstInput.Output;

                byte[] dummyReturnOutputBytes = CborSerializer.Serialize(dummyReturnOutput);
                ulong estimatedMinLovelaceForReturn = FeeUtil.CalculateMinimumLovelace(
                    (ulong)builder.Pparams!.AdaPerUTxOByte!,
                    dummyReturnOutputBytes
                );

                // Add buffer to ensure we have enough for return
                ulong totalCollateralNeeded = totalCollateral + estimatedMinLovelaceForReturn + (estimatedMinLovelaceForReturn / 2);

                // Use coin selection to get sufficient collateral with buffer
                List<Value> collateralRequirement = [new Lovelace(totalCollateralNeeded)];
                int maxCollateralInputs = (int)(builder.Pparams!.MaxCollateralInputs ?? 3);

                CoinSelectionResult collateralSelection;
                try
                {
                    collateralSelection = CoinSelectionUtil.LargestFirstAlgorithm(
                        availableInputs,
                        collateralRequirement,
                        maxCollateralInputs
                    );
                }
                catch (InvalidOperationException)
                {
                    // Fallback: try with just the required collateral amount
                    collateralSelection = CoinSelectionUtil.LargestFirstAlgorithm(
                        availableInputs,
                        [new Lovelace(totalCollateral)],
                        maxCollateralInputs
                    );
                }

                List<ResolvedInput> collateralInputs = collateralSelection.Inputs;

                // Add all selected collateral inputs to the builder
                foreach (ResolvedInput input in collateralInputs)
                {
                    _ = builder.AddCollateral(input.Outref);
                }

                // Calculate totals
                ulong totalCollateralInputLovelace = (ulong)collateralInputs.Sum(i => (long)i.Output.Amount().Lovelace());

                if (totalCollateralInputLovelace < totalCollateral)
                {
                    throw new InvalidOperationException(
                        $"Selected collateral inputs insufficient. Need {totalCollateral} lovelace but only have {totalCollateralInputLovelace} lovelace."
                    );
                }

                ulong returnLovelace = totalCollateralInputLovelace - totalCollateral;

                // Aggregate all assets from collateral inputs
                Dictionary<byte[], TokenBundleOutput> aggregatedAssets = new(ByteArrayEqualityComparer.Instance);
                foreach (ResolvedInput collateralInput in collateralInputs)
                {
                    if (collateralInput.Output.Amount() is LovelaceWithMultiAsset multiAsset && multiAsset.MultiAsset?.Value != null)
                    {
                        foreach ((byte[] policyId, TokenBundleOutput tokenBundle) in multiAsset.MultiAsset.Value)
                        {
                            if (!aggregatedAssets.TryGetValue(policyId, out TokenBundleOutput? existingBundle))
                            {
                                aggregatedAssets[policyId] = tokenBundle;
                            }
                            else
                            {
                                // Merge token bundles
                                Dictionary<byte[], ulong> mergedTokens = new(ByteArrayEqualityComparer.Instance);
                                foreach ((byte[] name, ulong amount) in existingBundle.Value)
                                {
                                    mergedTokens[name] = amount;
                                }
                                foreach ((byte[] name, ulong amount) in tokenBundle.Value)
                                {
                                    mergedTokens[name] = mergedTokens.TryGetValue(name, out ulong existing) ? existing + amount : amount;
                                }
                                aggregatedAssets[policyId] = new TokenBundleOutput(mergedTokens);
                            }
                        }
                    }
                }

                // Build return value
                Value returnValue = aggregatedAssets.Count > 0
                    ? new LovelaceWithMultiAsset(
                        new Lovelace(returnLovelace),
                        new MultiAssetOutput(aggregatedAssets)
                    )
                    : new Lovelace(returnLovelace);

                // Create return output using first collateral's address
                ResolvedInput firstCollateral = collateralInputs[0];
                TransactionOutput returnOutput = firstCollateral.Output switch
                {
                    AlonzoTransactionOutput alonzo => new AlonzoTransactionOutput(
                        alonzo.Address,
                        returnValue,
                        null
                    ),
                    PostAlonzoTransactionOutput postAlonzo => new AlonzoTransactionOutput(
                        postAlonzo.Address!,
                        returnValue,
                        null
                    ),
                    _ => throw new InvalidOperationException("Invalid transaction output type")
                };

                // Final verification of minimum ADA requirement
                byte[] returnOutputBytes = CborSerializer.Serialize(returnOutput);
                ulong minLovelaceRequired = FeeUtil.CalculateMinimumLovelace(
                    (ulong)builder.Pparams!.AdaPerUTxOByte!,
                    returnOutputBytes
                );

                if (returnLovelace < minLovelaceRequired)
                {
                    throw new InvalidOperationException(
                        $"Collateral return output ({returnLovelace} lovelace) is below minimum ADA requirement ({minLovelaceRequired} lovelace). Available inputs cannot provide sufficient collateral with adequate return."
                    );
                }

                _ = builder.SetCollateralReturn(returnOutput);
            }

            iterations++;
        }

        if (iterations >= maxIterations)
        {
            throw new InvalidOperationException("Fee calculation did not converge after maximum iterations.");
        }

        if (defaultFee > 0)
        {
            fee = defaultFee;
        }

        _ = builder.SetFee(fee);

        if (builder.ChangeOutput is null)
        {
            return builder;
        }

        List<TransactionOutput> outputs = [.. builder.Body.Outputs.GetValue()];

        decimal updatedChangeLovelace = builder.ChangeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount switch
            {
                Lovelace value => (decimal)value.Value - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value - fee,
                _ => throw new InvalidOperationException("Invalid change output type")
            },
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount switch
            {
                Lovelace value => value.Value - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value - fee,
                _ => throw new InvalidOperationException("Invalid change output type")
            },
            _ => throw new InvalidOperationException("Invalid change output type")
        };

        if (updatedChangeLovelace < 0)
        {
            updatedChangeLovelace = 0;
        }

        Value changeValue = new Lovelace((ulong)updatedChangeLovelace);

        Value changeOutputValue = builder.ChangeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount,
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount,
            _ => throw new InvalidOperationException("Invalid change output type")
        };

        if (changeOutputValue is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = new LovelaceWithMultiAsset(new Lovelace((ulong)updatedChangeLovelace), lovelaceWithMultiAsset.MultiAsset);
        }

        TransactionOutput? updatedChangeOutput = null;

        if (updatedChangeLovelace > 0)
        {
            if (builder.ChangeOutput is AlonzoTransactionOutput change)
            {
                updatedChangeOutput = new AlonzoTransactionOutput(
                    change.Address,
                    changeValue,
                    change.DatumHash
                );

            }
            else if (builder.ChangeOutput is PostAlonzoTransactionOutput postAlonzoChange)
            {
                updatedChangeOutput = new PostAlonzoTransactionOutput(
                    postAlonzoChange.Address!,
                    changeValue,
                    postAlonzoChange.Datum,
                    postAlonzoChange.ScriptRef
                );
            }
        }

        outputs.RemoveAt(outputs.Count - 1);

        if (updatedChangeOutput is not null)
        {
            byte[] updatedChangeOutputBytes = CborSerializer.Serialize(updatedChangeOutput);
            ulong minLovelace = FeeUtil.CalculateMinimumLovelace((ulong)builder.Pparams!.AdaPerUTxOByte!, updatedChangeOutputBytes);

            if (updatedChangeLovelace < minLovelace)
            {
                _ = builder.SetFee(fee + (ulong)updatedChangeLovelace);
            }
            else
            {
                outputs.Add(updatedChangeOutput);
            }
        }

        _ = builder.SetOutputs(outputs);

        return builder;
    }

    /// <summary>
    /// Evaluates Plutus scripts in the transaction and updates execution unit budgets.
    /// </summary>
    /// <param name="builder">The transaction builder.</param>
    /// <param name="utxos">The resolved UTxOs for evaluation context.</param>
    /// <param name="networkType">The network type for evaluation.</param>
    /// <returns>The transaction builder with updated execution units.</returns>
    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos, NetworkType networkType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(utxos);

        CborDefList<ResolvedInput> utxoCbor = new(utxos);
        byte[] utxoCborBytes = CborSerializer.Serialize<CborMaybeIndefList<ResolvedInput>>(utxoCbor);
        Transaction transaction = builder.Build();
        byte[] txCborBytes = CborSerializer.Serialize(transaction);
        IReadOnlyList<Chrysalis.Plutus.VM.Models.EvaluationResult> evalResult = Evaluator.EvaluateTx(txCborBytes, utxoCborBytes, networkType);
        Redeemers? previousRedeemers = builder.WitnessSet.Redeemers;


        switch (previousRedeemers)
        {
            case RedeemerList redeemersList:
                List<RedeemerEntry> updatedRedeemersList = [];
                foreach (RedeemerEntry redeemer in redeemersList.Value)
                {
                    foreach (Chrysalis.Plutus.VM.Models.EvaluationResult result in evalResult)
                    {
                        if (redeemer.Tag == (int)result.RedeemerTag && redeemer.Index == result.Index)
                        {
                            ExUnits exUnits = new(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersList.Add(new RedeemerEntry(redeemer.Tag, redeemer.Index, redeemer.Data, exUnits));
                        }
                    }
                }
                _ = builder.SetRedeemers(new RedeemerList(updatedRedeemersList));
                break;
            case RedeemerMap redeemersMap:
                Dictionary<RedeemerKey, RedeemerValue> updatedRedeemersMap = [];
                foreach (KeyValuePair<RedeemerKey, RedeemerValue> kvp in redeemersMap.Value)
                {
                    foreach (Chrysalis.Plutus.VM.Models.EvaluationResult result in evalResult)
                    {
                        if (kvp.Key.Tag == (int)result.RedeemerTag && kvp.Key.Index == result.Index)
                        {
                            ExUnits exUnits = new(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersMap.Add(new RedeemerKey(kvp.Key.Tag, kvp.Key.Index), new RedeemerValue(kvp.Value.Data, exUnits));
                        }
                    }
                }
                _ = builder.SetRedeemers(new RedeemerMap(updatedRedeemersMap));
                break;
            default:
                break;
        }


        return builder;
    }
}
