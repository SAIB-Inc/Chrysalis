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
    /// <summary>
    /// Calculates and sets the transaction fee, including script fees and collateral handling.
    /// </summary>
    /// <param name="builder">The transaction builder.</param>
    /// <param name="scripts">The scripts used in the transaction.</param>
    /// <param name="defaultFee">An optional fixed fee override.</param>
    /// <param name="mockWitnessFee">Number of mock witnesses for fee estimation.</param>
    /// <param name="availableInputs">Available inputs for collateral selection.</param>
    /// <returns>The transaction builder with fee set.</returns>
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, List<IScript> scripts, ulong defaultFee = 0, int mockWitnessFee = 1, List<ResolvedInput>? availableInputs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scripts);

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;
        if (builder.Redeemers is not null)
        {
            _ = builder.SetTotalCollateral(2000000UL);

            if (scripts.Count <= 0)
            {
                throw new ArgumentException("Missing script", nameof(scripts));
            }

            int langVersion = scripts[0].Version() - 1;
            CostMdls costMdls = builder.Pparams!.CostModelsForScriptLanguage!;
            ICborMaybeIndefList<long>? usedLanguage = langVersion switch
            {
                0 => costMdls.PlutusV1,
                1 => costMdls.PlutusV2,
                2 => costMdls.PlutusV3,
                _ => throw new ArgumentException($"Unsupported script language version: {langVersion}")
            };
            CostMdls costModel = new(
                langVersion == 0 ? usedLanguage : null,
                langVersion == 1 ? usedLanguage : null,
                langVersion == 2 ? usedLanguage : null
            );
            byte[] costModelBytes = CborSerializer.Serialize(costModel);
            PostAlonzoTransactionWitnessSet witnessSet = builder.BuildWitnessSet();
            byte[] scriptDataHash = DataHashUtil.CalculateScriptDataHash(builder.Redeemers, witnessSet.PlutusDataSet, costModelBytes);
            _ = builder.SetScriptDataHash(scriptDataHash);

            ulong scriptCostPerByte = builder.Pparams!.MinFeeRefScriptCostPerByte!.Numerator / builder.Pparams.MinFeeRefScriptCostPerByte!.Denominator;

            // Tiered pricing applies to TOTAL script size, not per-script
            int totalScriptSize = scripts.Sum(script => script.Bytes().Length);
            scriptFee = FeeUtil.CalculateReferenceScriptFee(totalScriptSize, scriptCostPerByte);

            RationalNumber memUnitsCost = new(builder.Pparams!.ExecutionCosts!.Value.MemPrice.Numerator, builder.Pparams.ExecutionCosts!.Value.MemPrice.Denominator);
            RationalNumber stepUnitsCost = new(builder.Pparams.ExecutionCosts!.Value.StepPrice.Numerator, builder.Pparams.ExecutionCosts!.Value.StepPrice.Denominator);
            scriptExecutionFee = FeeUtil.CalculateScriptExecutionFee(builder.Redeemers, memUnitsCost, stepUnitsCost);

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
            ITransaction draftTx = builder.Build();
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
            if (builder.TotalCollateral is not null && availableInputs != null && availableInputs.Count > 0)
            {
                ulong totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.Pparams!.CollateralPercentage!.Value);
                _ = builder.SetTotalCollateral(totalCollateral);

                // Clear any existing collateral settings to start fresh
                _ = builder.ClearCollateral();
                _ = builder.ClearCollateralReturn();

                // Estimate minimum ADA needed for return output
                ResolvedInput firstInput = availableInputs[0];
                ITransactionOutput dummyReturnOutput = firstInput.Output;

                byte[] dummyReturnOutputBytes = CborSerializer.Serialize(dummyReturnOutput);
                ulong estimatedMinLovelaceForReturn = FeeUtil.CalculateMinimumLovelace(
                    (ulong)builder.Pparams!.AdaPerUTxOByte!,
                    dummyReturnOutputBytes
                );

                // Add buffer to ensure we have enough for return
                ulong totalCollateralNeeded = totalCollateral + estimatedMinLovelaceForReturn + (estimatedMinLovelaceForReturn / 2);

                // Use coin selection to get sufficient collateral with buffer
                List<IValue> collateralRequirement = [CborFactory.CreateLovelace(totalCollateralNeeded)];
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
                        [CborFactory.CreateLovelace(totalCollateral)],
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
                Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> aggregatedAssets = new(ReadOnlyMemoryComparer.Instance);
                foreach (ResolvedInput collateralInput in collateralInputs)
                {
                    if (collateralInput.Output.Amount() is LovelaceWithMultiAsset multiAsset)
                    {
                        foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput tokenBundle) in multiAsset.MultiAsset.Value)
                        {
                            if (!aggregatedAssets.TryGetValue(policyId, out TokenBundleOutput existingBundle))
                            {
                                aggregatedAssets[policyId] = tokenBundle;
                            }
                            else
                            {
                                // Merge token bundles
                                Dictionary<ReadOnlyMemory<byte>, ulong> mergedTokens = new(ReadOnlyMemoryComparer.Instance);
                                foreach ((ReadOnlyMemory<byte> name, ulong amount) in existingBundle.Value)
                                {
                                    mergedTokens[name] = amount;
                                }
                                foreach ((ReadOnlyMemory<byte> name, ulong amount) in tokenBundle.Value)
                                {
                                    mergedTokens[name] = mergedTokens.TryGetValue(name, out ulong existing) ? existing + amount : amount;
                                }
                                aggregatedAssets[policyId] = CborFactory.CreateTokenBundleOutput(mergedTokens);
                            }
                        }
                    }
                }

                // Build return value
                IValue returnValue = aggregatedAssets.Count > 0
                    ? CborFactory.CreateLovelaceWithMultiAsset(
                        returnLovelace,
                        CborFactory.CreateMultiAssetOutput(aggregatedAssets)
                    )
                    : CborFactory.CreateLovelace(returnLovelace);

                // Create return output using first collateral's address
                ResolvedInput firstCollateral = collateralInputs[0];
                ITransactionOutput returnOutput = firstCollateral.Output switch
                {
                    AlonzoTransactionOutput alonzo => CborFactory.CreateAlonzoTransactionOutput(
                        alonzo.Address,
                        returnValue,
                        null
                    ),
                    PostAlonzoTransactionOutput postAlonzo => CborFactory.CreateAlonzoTransactionOutput(
                        postAlonzo.Address,
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

        List<ITransactionOutput> outputs = [.. builder.Outputs];

        decimal updatedChangeLovelace = builder.ChangeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount switch
            {
                Lovelace value => (decimal)value.Amount - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.Amount - fee,
                _ => throw new InvalidOperationException("Invalid change output type")
            },
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount switch
            {
                Lovelace value => value.Amount - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.Amount - fee,
                _ => throw new InvalidOperationException("Invalid change output type")
            },
            _ => throw new InvalidOperationException("Invalid change output type")
        };

        if (updatedChangeLovelace < 0)
        {
            updatedChangeLovelace = 0;
        }

        IValue changeValue = CborFactory.CreateLovelace((ulong)updatedChangeLovelace);

        IValue changeOutputValue = builder.ChangeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount,
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount,
            _ => throw new InvalidOperationException("Invalid change output type")
        };

        if (changeOutputValue is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = CborFactory.CreateLovelaceWithMultiAsset((ulong)updatedChangeLovelace, lovelaceWithMultiAsset.MultiAsset);
        }

        ITransactionOutput? updatedChangeOutput = null;

        if (updatedChangeLovelace > 0)
        {
            if (builder.ChangeOutput is AlonzoTransactionOutput change)
            {
                updatedChangeOutput = CborFactory.CreateAlonzoTransactionOutput(
                    change.Address,
                    changeValue,
                    change.DatumHash
                );

            }
            else if (builder.ChangeOutput is PostAlonzoTransactionOutput postAlonzoChange)
            {
                updatedChangeOutput = CborFactory.CreatePostAlonzoTransactionOutput(
                    postAlonzoChange.Address,
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
    /// Uses the managed Plutus VM with ScriptContext builder.
    /// </summary>
    /// <param name="builder">The transaction builder.</param>
    /// <param name="utxos">The resolved UTxOs for evaluation context.</param>
    /// <param name="slotConfig">The slot-to-posix-time configuration for the network.</param>
    /// <returns>The transaction builder with updated execution units.</returns>
    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos, SlotNetworkConfig slotConfig)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(utxos);
        ArgumentNullException.ThrowIfNull(slotConfig);

        ConwayTransactionBody body = builder.BuildBody();
        PostAlonzoTransactionWitnessSet witnessSet = builder.BuildWitnessSet();

        IReadOnlyList<Plutus.VM.Models.EvaluationResult> evalResult =
            ScriptContextBuilder.EvaluateTx(body, witnessSet, utxos, slotConfig);

        IRedeemers? previousRedeemers = builder.Redeemers;

        switch (previousRedeemers)
        {
            case RedeemerList redeemersList:
                List<RedeemerEntry> updatedRedeemersList = [];
                foreach (RedeemerEntry redeemer in redeemersList.Value)
                {
                    foreach (Plutus.VM.Models.EvaluationResult result in evalResult)
                    {
                        if (redeemer.Tag == result.RedeemerTag && redeemer.Index == result.Index)
                        {
                            ExUnits exUnits = CborFactory.CreateExUnits(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersList.Add(CborFactory.CreateRedeemerEntry(redeemer.Tag, redeemer.Index, redeemer.Data, exUnits));
                        }
                    }
                }
                _ = builder.SetRedeemers(CborFactory.CreateRedeemerList(updatedRedeemersList));
                break;
            case RedeemerMap redeemersMap:
                Dictionary<RedeemerKey, RedeemerValue> updatedRedeemersMap = [];
                foreach (KeyValuePair<RedeemerKey, RedeemerValue> kvp in redeemersMap.Value)
                {
                    foreach (Plutus.VM.Models.EvaluationResult result in evalResult)
                    {
                        if (kvp.Key.Tag == result.RedeemerTag && kvp.Key.Index == result.Index)
                        {
                            ExUnits exUnits = CborFactory.CreateExUnits(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersMap.Add(CborFactory.CreateRedeemerKey(kvp.Key.Tag, kvp.Key.Index), CborFactory.CreateRedeemerValue(kvp.Value.Data, exUnits));
                        }
                    }
                }
                _ = builder.SetRedeemers(CborFactory.CreateRedeemerMap(updatedRedeemersMap));
                break;
            default:
                break;
        }

        return builder;
    }
}
