using System.Dynamic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.TransactionBuilding.Extensions;

public static class TransactionBuilderExtensions
{
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, byte[] scriptBytes = default!, int mockWitnessFee = 1)
    {

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;
        // Script data hash calculation and script fee calculation
        if (builder.witnessBuilder.redeemers is not null)
        {
            builder.SetTotalCollateral(2000000UL);

            var usedLanguage = builder.pparams!.CostModelsForScriptLanguage!.Value[2];
            var costModel = new CostMdls(new Dictionary<int, CborIndefList<long>>(){
                 { 2, usedLanguage }
            });
            var costModelBytes = CborSerializer.Serialize(costModel);
            var scriptDataHash = ScriptDataHashUtil.CalculateScriptDataHash(builder.witnessBuilder.redeemers, new PlutusList([]), costModelBytes);
            builder.SetScriptDataHash(scriptDataHash);

            ulong scriptCostPerByte = builder.pparams!.MinFeeRefScriptCostPerByte!.Numerator / builder.pparams.MinFeeRefScriptCostPerByte!.Denominator;
            scriptFee = FeeUtil.CalculateReferenceScriptFee(scriptBytes, scriptCostPerByte);

            RationalNumber memUnitsCost = new(builder.pparams!.ExecutionCosts!.MemPrice!.Numerator!, builder.pparams.ExecutionCosts!.MemPrice!.Denominator!);
            RationalNumber stepUnitsCost = new(builder.pparams.ExecutionCosts!.StepPrice!.Numerator!, builder.pparams.ExecutionCosts!.StepPrice!.Denominator!);
            scriptExecutionFee = FeeUtil.CalculateScriptExecutionFee(builder.witnessBuilder.redeemers, memUnitsCost, stepUnitsCost);

        }

        // fee and change calculation
        Transaction draftTx = builder.Build();
        var draftTxCborBytes = CborSerializer.Serialize(draftTx);
        ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;
        var fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.pparams!.MinFeeA!.Value, builder!.pparams.MinFeeB!.Value, mockWitnessFee) + scriptFee + scriptExecutionFee;

        if (builder.bodyBuilder.totalCollateral is not null)
        {
            var totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.pparams!.CollateralPercentage!.Value);
            builder.SetTotalCollateral(totalCollateral);
            Address address = builder.bodyBuilder.collateralReturn switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address,
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Address!,
                _ => throw new Exception("Invalid collateral return type")
            };

            ulong lovelace = builder.bodyBuilder.collateralReturn switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Amount switch
                {
                    Lovelace value => value.Value,
                    LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value,
                    _ => 0
                },
                _ => 0
            };
            builder.SetCollateralReturn(new AlonzoTransactionOutput(
                address,
                new Lovelace(lovelace - totalCollateral), null));
        }

        var outputs = builder.bodyBuilder.Outputs;
        var changeOutput = outputs.Find(output => output.Item2);

        Lovelace updatedChangeLovelace = changeOutput.Item1 switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount switch
            {
                Lovelace value => new Lovelace(value.Value - fee),
                LovelaceWithMultiAsset multiAsset => new Lovelace(multiAsset.LovelaceValue.Value - fee),
                _ => throw new Exception("Invalid change output type")
            },
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount switch
            {
                Lovelace value => new Lovelace(value.Value - fee),
                LovelaceWithMultiAsset multiAsset => new Lovelace(multiAsset.LovelaceValue.Value - fee),
                _ => throw new Exception("Invalid change output type")
            },
            _ => throw new Exception("Invalid change output type")
        };

        Value changeValue = updatedChangeLovelace;

        Value changeOutputValue = changeOutput.Item1 switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount,
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount,
            _ => throw new Exception("Invalid change output type")
        };

        if (changeOutputValue is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = new LovelaceWithMultiAsset(updatedChangeLovelace, lovelaceWithMultiAsset.MultiAsset);
        }

        TransactionOutput? updatedChangeOutput = null;

        if (changeOutput.Item1 is AlonzoTransactionOutput change)
        {
            updatedChangeOutput = new AlonzoTransactionOutput(
                change.Address,
                changeValue,
                change.DatumHash
            );

            builder.bodyBuilder.Outputs.Remove(changeOutput);
            builder.AddOutput(updatedChangeOutput, true);
        }
        else if (changeOutput.Item1 is PostAlonzoTransactionOutput postAlonzoChange)
        {
            updatedChangeOutput = new PostAlonzoTransactionOutput(
                postAlonzoChange.Address!,
                changeValue,
                postAlonzoChange.Datum,
                postAlonzoChange.ScriptRef
            );

            builder.bodyBuilder.Outputs.Remove(changeOutput);
            builder.AddOutput(updatedChangeOutput, true);

        }

        builder.SetFee(fee);


        return builder;
    }

    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos)
    {
        CborDefList<ResolvedInput> utxoCbor = new(utxos);
        var utxoCborBytes = CborSerializer.Serialize<CborMaybeIndefList<ResolvedInput>>(utxoCbor);
        Transaction transaction = builder.Build();
        Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(transaction)));  
        var txCborBytes = CborSerializer.Serialize(transaction);
        var evalResult = Evaluator.EvaluateTx(txCborBytes, utxoCborBytes);
        var previousRedeemers = builder.witnessBuilder.redeemers;


        switch (previousRedeemers)
        {
            case RedeemerList redeemersList:
                List<RedeemerEntry> updatedRedeemersList = [];
                foreach (var redeemer in redeemersList.Value)
                {
                    foreach (var result in evalResult)
                    {
                        if (redeemer.Tag == (int)result.RedeemerTag && redeemer.Index == result.Index)
                        {
                            ExUnits exUnits = new(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersList.Add(new RedeemerEntry(redeemer.Tag, redeemer.Index, redeemer.Data, exUnits));
                        }
                    }
                }
                builder.SetRedeemers(new RedeemerList(updatedRedeemersList));
                break;
            case RedeemerMap redeemersMap:
                Dictionary<RedeemerKey, RedeemerValue> updatedRedeemersMap = [];
                foreach (var kvp in redeemersMap.Value)
                {
                    foreach (var result in evalResult)
                    {
                        if (kvp.Key.Tag == (int)result.RedeemerTag && kvp.Key.Index == result.Index)
                        {
                            ExUnits exUnits = new(140000, 100000000);
                            updatedRedeemersMap.Add(new RedeemerKey(kvp.Key.Tag, kvp.Key.Index), new RedeemerValue(kvp.Value.Data, exUnits));
                        }
                    }
                }
                builder.SetRedeemers(new RedeemerMap(updatedRedeemersMap));
                break;
        }


        return builder;
    }

    public static TransactionBuilder SetCollateral(this TransactionBuilder builder, ResolvedInput collateral)
    {
        builder.bodyBuilder.AddCollateral(collateral.Outref);
        builder.bodyBuilder.SetCollateralReturn(collateral.Output);
        return builder;
    }

}