using System.Dynamic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.TransactionBuilding.Extensions;

public static class TransactionBuilderExtensions
{
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, byte[] scriptBytes, int mockWitnessFee = 1)
    {   

        ulong scriptFee = 0;
        var sortedInputs = builder.bodyBuilder.Inputs.OrderBy(input => Convert.ToHexString(input.TransactionId.Value) + input.Index.Value).ToList();
        builder.bodyBuilder.Inputs = sortedInputs;
        // Script data hash calculation and script fee calculation
        if (builder.witnessBuilder.redeemers is not null)
        {
            builder.SetTotalCollateral(2000000UL);
            var usedLanguage = builder.pparams!.CostModelsForScriptLanguage!.Value[new CborInt(2)];
            var costModel = new CostMdls(new Dictionary<CborInt, CborIndefList<CborLong>>(){
                 { new CborInt(2), usedLanguage }
            });
            var costModelBytes = CborSerializer.Serialize(costModel);
            var scriptDataHash = ScriptDataHashUtil.CalculateScriptDataHash(builder.witnessBuilder.redeemers, new PlutusList([]), costModelBytes);
            builder.SetScriptDataHash(scriptDataHash);

            ulong scriptCostPerByte = builder.pparams!.MinFeeRefScriptCostPerByte!.Numerator / builder.pparams.MinFeeRefScriptCostPerByte!.Denominator;
            scriptFee = FeeUtil.CalculateReferenceScriptFee(scriptBytes, scriptCostPerByte);
        }

        // fee and change calculation
        var draftTx = builder.Build();
        var draftTxCborBytes = CborSerializer.Serialize(draftTx);
        ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;
        var fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.pparams!.MinFeeA!.Value, builder!.pparams.MinFeeB!.Value, mockWitnessFee) + scriptFee;

        var totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.pparams!.CollateralPercentage!.Value);
        builder.SetTotalCollateral(totalCollateral);

        var outputs = builder.bodyBuilder.Outputs;
        var changeOutput = outputs.Find(output => output.Item2);


        var updatedChangeLovelace = new Lovelace((changeOutput.Item1.Amount()!.Lovelace() - fee)!.Value);
        Value changeValue = updatedChangeLovelace;
        if (changeOutput.Item1.Amount() is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = new LovelaceWithMultiAsset(updatedChangeLovelace, lovelaceWithMultiAsset.MultiAsset);
        }

        var updatedChangeOutput = new PostAlonzoTransactionOutput(
            changeOutput.Item1.Address()!,
            changeValue,
            changeOutput.Item1.Datum,
            changeOutput.Item1.ScriptRef
            );

        builder.bodyBuilder.Outputs.Remove(changeOutput);
        builder.AddOutput(updatedChangeOutput, true).SetFee(fee);




        return builder;
    }

    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos)
    {
        var utxoCborBytes = CborSerializer.Serialize(new CborDefList<ResolvedInput>(utxos));
        var txCborBytes = CborSerializer.Serialize(builder.Build());
        var evalResult = Evaluator.EvaluateTx(txCborBytes, utxoCborBytes);
        Console.WriteLine(evalResult);
        return builder;
    }


}