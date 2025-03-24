using System.Dynamic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Chrysalis.Cbor.Cardano.Extensions;
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

            var usedLanguage = builder.pparams!.CostModelsForScriptLanguage!.Value[new CborInt(2)];
            var costModel = new CostMdls(new Dictionary<CborInt, CborDefList<CborLong>>(){
                 { new CborInt(2), usedLanguage }
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
        var draftTx = builder.Build();
        var draftTxCborBytes = CborSerializer.Serialize(draftTx);
        ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;
        var fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.pparams!.MinFeeA!.Value, builder!.pparams.MinFeeB!.Value, mockWitnessFee) + scriptFee + scriptExecutionFee;

        if (builder.bodyBuilder.totalCollateral is not null)
        {
            var totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.pparams!.CollateralPercentage!.Value);
            builder.SetTotalCollateral(totalCollateral);
            builder.SetCollateralReturn(new AlonzoTransactionOutput(
                builder.bodyBuilder!.collateralReturn!.Address()!,
                new Lovelace(builder.bodyBuilder!.collateralReturn!.Lovelace()!.Value - totalCollateral), null));
        }

        var outputs = builder.bodyBuilder.Outputs;
        var changeOutput = outputs.Find(output => output.Item2);


        var updatedChangeLovelace = new Lovelace((changeOutput.Item1.Amount()!.Lovelace() - fee)!.Value);
        Value changeValue = updatedChangeLovelace;

        if (changeOutput.Item1.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = new LovelaceWithMultiAsset(updatedChangeLovelace, lovelaceWithMultiAsset.MultiAsset);
        }

        TransactionOutput? updatedChangeOutput = null;

        if (changeOutput.Item1 is AlonzoTransactionOutput change)
        {
            updatedChangeOutput = new AlonzoTransactionOutput(
                change.Address()!,
                changeValue,
                change.Datum() is not null ? new CborBytes(change.Datum()!) : null
            );

            builder.bodyBuilder.Outputs.Remove(changeOutput);
            builder.AddOutput(updatedChangeOutput, true);
        }
        else if (changeOutput.Item1 is PostAlonzoTransactionOutput postAlonzoChange)
        {
            updatedChangeOutput = new PostAlonzoTransactionOutput(
                postAlonzoChange.Address()!,
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
        var utxoCborBytes = CborSerializer.Serialize(new CborDefList<ResolvedInput>(utxos));
        var txCborBytes = CborSerializer.Serialize(builder.Build());
        var evalResult = Evaluator.EvaluateTx(txCborBytes, utxoCborBytes);
        var previousRedeemers = builder.witnessBuilder.redeemers;
        foreach (var result in evalResult)
        {
            switch (previousRedeemers)
            {
                case RedeemerList redeemersList:
                    List<RedeemerEntry> updatedRedeemersList = [];
                    foreach (var redeemer in redeemersList.Value)
                    {
                        ExUnits exUnits = redeemer.ExUnits;
                        if (redeemer.Tag.Value == (int)result.RedeemerTag && redeemer.Index.Value == result.Index)
                        {
                            exUnits = new(new CborUlong(result.ExUnits.Mem), new CborUlong(result.ExUnits.Steps));
                        }
                        updatedRedeemersList.Add(new RedeemerEntry(redeemer.Tag, redeemer.Index, redeemer.Data, exUnits));
                    }
                    builder.SetRedeemers(new RedeemerList(updatedRedeemersList));
                    break;
                case RedeemerMap redeemersMap:
                    Dictionary<RedeemerKey, RedeemerValue> updatedRedeemersMap = [];
                    foreach (var kvp in redeemersMap.Value)
                    {
                        ExUnits exUnits = kvp.Value.ExUnits;
                        if (kvp.Key.Tag.Value == (int)result.RedeemerTag && kvp.Key.Index.Value == result.Index)
                        {
                            exUnits = new(new CborUlong(result.ExUnits.Mem * 10), new CborUlong(result.ExUnits.Steps * 10));
                        }
                        updatedRedeemersMap.Add(new RedeemerKey(kvp.Key.Tag, kvp.Key.Index), new RedeemerValue(kvp.Value.Data, exUnits));
                    }
                    builder.SetRedeemers(new RedeemerMap(updatedRedeemersMap));
                    break;
            }

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