using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.TransactionBuilding.Extensions;

public static class TransactionBuilderExtensions
{
    public static TransactionBuilder CalculateFee(this TransactionBuilder builder, int mockWitnessFee = 1)
    {
        var draftTx = builder.Build();
        var draftTxCborBytes = CborSerializer.Serialize(draftTx);
        ulong draftTxCborLength = (ulong)draftTxCborBytes.Length;
        var fee = FeeUtil.CalculateFeeWithWitness(draftTxCborLength, builder.pparams!.MinFeeA!.Value, builder!.pparams.MinFeeB!.Value, mockWitnessFee);

        var outputs = builder.bodyBuilder.Outputs;
        var changeOutput = outputs.Find(output => output.Item2);
        var updatedChangeOutput = new AlonzoTransactionOutput(
            changeOutput.Item1.Address()!,
            new Lovelace((changeOutput.Item1.Amount()!.Lovelace() - fee)!.Value), changeOutput.Item1.Datum() is not null ? new CborBytes(changeOutput.Item1.Datum()!) : null);

        builder.bodyBuilder.Outputs.Remove(changeOutput);
        builder.AddOutput(updatedChangeOutput, true).SetFee(fee);

        return builder;
    }

}