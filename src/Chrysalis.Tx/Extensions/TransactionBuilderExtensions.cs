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

namespace Chrysalis.Tx.Extensions;

public static class TransactionBuilderExtensions
{
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, List<Script> scripts, ulong defaultFee = 0, int mockWitnessFee = 1)
    {

        ulong scriptFee = 0;
        ulong scriptExecutionFee = 0;
        if (builder.witnessSet.Redeemers is not null)
        {
            builder.SetTotalCollateral(2000000UL);

            if (scripts.Count <= 0)
            {
                throw new ArgumentNullException(nameof(scripts), "Missing script");
            }

            var usedLanguage = builder.pparams!.CostModelsForScriptLanguage!.Value[scripts[0].Version() - 1];
            var costModel = new CostMdls(new Dictionary<int, CborDefList<long>>(){
                 { scripts[0].Version() - 1, usedLanguage }
            });
            var costModelBytes = CborSerializer.Serialize(costModel);
            var scriptDataHash = DataHashUtil.CalculateScriptDataHash(builder.witnessSet.Redeemers, builder.witnessSet.PlutusDataSet?.GetValue() as PlutusList, costModelBytes);
            builder.SetScriptDataHash(scriptDataHash);

            ulong scriptCostPerByte = builder.pparams!.MinFeeRefScriptCostPerByte!.Numerator / builder.pparams.MinFeeRefScriptCostPerByte!.Denominator;
            scriptFee = (ulong)scripts.Sum(script => (decimal)FeeUtil.CalculateReferenceScriptFee(script.Bytes(), scriptCostPerByte * 5));

            RationalNumber memUnitsCost = new(builder.pparams!.ExecutionCosts!.MemPrice!.Numerator!, builder.pparams.ExecutionCosts!.MemPrice!.Denominator!);
            RationalNumber stepUnitsCost = new(builder.pparams.ExecutionCosts!.StepPrice!.Numerator!, builder.pparams.ExecutionCosts!.StepPrice!.Denominator!);
            scriptExecutionFee = FeeUtil.CalculateScriptExecutionFee(builder.witnessSet.Redeemers, memUnitsCost, stepUnitsCost);

        }

        builder.SetFee(defaultFee == 0 ? 2000000UL : defaultFee);
        // fee and change calculation
        Transaction draftTx = builder.Build();
        var draftTxCborBytes = CborSerializer.Serialize(draftTx);
        ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;

        var fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.pparams!.MinFeeA!.Value, builder!.pparams.MinFeeB!.Value, mockWitnessFee) + scriptFee + scriptExecutionFee;

        if (builder.body.TotalCollateral is not null)
        {
            var totalCollateral = FeeUtil.CalculateRequiredCollateral(fee, builder.pparams!.CollateralPercentage!.Value);
            builder.SetTotalCollateral(totalCollateral);

            Address address = builder.body.CollateralReturn switch
            {
                AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address,
                PostAlonzoTransactionOutput postAlonzoTransactionOutput => postAlonzoTransactionOutput.Address!,
                _ => throw new Exception("Invalid collateral return type")
            };

            ulong lovelace = builder.body.CollateralReturn.Amount().Lovelace();
            builder.SetCollateralReturn(new AlonzoTransactionOutput(
                address,
                new Lovelace(lovelace - totalCollateral), null));
        }

        if (defaultFee > 0)
        {
            fee = defaultFee;
        }

        builder.SetFee(fee);

        if (builder.changeOutput is null)
        {
            return builder;
        }

        List<TransactionOutput> outputs = [.. builder.body.Outputs.GetValue()];

        decimal updatedChangeLovelace = builder.changeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount switch
            {
                Lovelace value => (decimal)value.Value - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value - fee,
                _ => throw new Exception("Invalid change output type")
            },
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount switch
            {
                Lovelace value => value.Value - fee,
                LovelaceWithMultiAsset multiAsset => multiAsset.LovelaceValue.Value - fee,
                _ => throw new Exception("Invalid change output type")
            },
            _ => throw new Exception("Invalid change output type")
        };

        if (updatedChangeLovelace < 0)
        {
            updatedChangeLovelace = 0;
        }

        Value changeValue = new Lovelace((ulong)updatedChangeLovelace);

        Value changeOutputValue = builder.changeOutput switch
        {
            AlonzoTransactionOutput alonzo => alonzo.Amount,
            PostAlonzoTransactionOutput postAlonzoChange => postAlonzoChange.Amount,
            _ => throw new Exception("Invalid change output type")
        };

        if (changeOutputValue is LovelaceWithMultiAsset lovelaceWithMultiAsset)
        {
            changeValue = new LovelaceWithMultiAsset(new Lovelace((ulong)updatedChangeLovelace), lovelaceWithMultiAsset.MultiAsset);
        }

        TransactionOutput? updatedChangeOutput = null;

        if (updatedChangeLovelace > 0)
        {
            if (builder.changeOutput is AlonzoTransactionOutput change)
            {
                updatedChangeOutput = new AlonzoTransactionOutput(
                    change.Address,
                    changeValue,
                    change.DatumHash
                );

            }
            else if (builder.changeOutput is PostAlonzoTransactionOutput postAlonzoChange)
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
            ulong minLovelace = FeeUtil.CalculateMinimumLovelace((ulong)builder.pparams!.AdaPerUTxOByte!, updatedChangeOutputBytes);

            if (updatedChangeLovelace < minLovelace)
            {
                builder.SetFee(fee + (ulong)updatedChangeLovelace);
            }
            else
            {
                outputs.Add(updatedChangeOutput);
            }
        }

        builder.SetOutputs(outputs);

        return builder;
    }

    public static TransactionBuilder Evaluate(this TransactionBuilder builder, List<ResolvedInput> utxos)
    {
        CborDefList<ResolvedInput> utxoCbor = new(utxos);
        var utxoCborBytes = CborSerializer.Serialize<CborMaybeIndefList<ResolvedInput>>(utxoCbor);
        Transaction transaction = builder.Build();
        var txCborBytes = CborSerializer.Serialize(transaction);
        var evalResult = Evaluator.EvaluateTx(txCborBytes, utxoCborBytes);
        var previousRedeemers = builder.witnessSet.Redeemers;


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
                            ExUnits exUnits = new(result.ExUnits.Mem, result.ExUnits.Steps);
                            updatedRedeemersMap.Add(new RedeemerKey(kvp.Key.Tag, kvp.Key.Index), new RedeemerValue(kvp.Value.Data, exUnits));
                        }
                    }
                }
                builder.SetRedeemers(new RedeemerMap(updatedRedeemersMap));
                break;
        }


        return builder;
    }
}