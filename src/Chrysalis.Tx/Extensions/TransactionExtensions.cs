using Chrysalis.Cbor.Cardano.Extensions;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Extensions;
public static class TransactionExtension
{
    public static PostMaryTransaction Sign(this PostMaryTransaction transaction, PrivateKey privateKey)
    {
        var txBodyBytes = CborSerializer.Serialize(transaction.TransactionBody);
        var signature = privateKey.Sign(HashUtil.Blake2b256(txBodyBytes));
        var vkeyWitness = new VKeyWitness(new CborBytes(privateKey.GetPublicKey().Key), new CborBytes(signature));
        var vKeyWitnesses = transaction.TransactionWitnessSet.VKeyWitnessSet()?.ToList() ?? [];
        vKeyWitnesses.Add(vkeyWitness);
       
        var newWitnessSet = new PostAlonzoTransactionWitnessSet(
            new CborDefListWithTag<VKeyWitness>(vKeyWitnesses),
            transaction.TransactionWitnessSet.NativeScriptSet()!.Any() ? new CborDefList<NativeScript>([.. transaction.TransactionWitnessSet.NativeScriptSet()!]) : null,
            transaction.TransactionWitnessSet.BootstrapWitnessSet()!.Any() ? new CborDefList<BootstrapWitness>([.. transaction.TransactionWitnessSet.BootstrapWitnessSet()!]) : null,
            transaction.TransactionWitnessSet.PlutusV1ScriptSet()!.Any() ? new CborDefList<CborBytes>([.. transaction.TransactionWitnessSet.PlutusV1ScriptSet()!.Select(e => new CborBytes(e))]) : null,
            transaction.TransactionWitnessSet.PlutusDataSet()!.Any() ? new CborDefList<PlutusData>([.. transaction.TransactionWitnessSet.PlutusDataSet()!]) : null,
            transaction.TransactionWitnessSet.Redeemers() is not null? transaction.TransactionWitnessSet.Redeemers()! : null,
            transaction.TransactionWitnessSet.PlutusV2ScriptSet()!.Any() ? new CborDefList<CborBytes>([.. transaction.TransactionWitnessSet.PlutusV2ScriptSet()!.Select(e => new CborBytes(e))]) : null,
            transaction.TransactionWitnessSet.PlutusV3ScriptSet()!.Any() ? new CborDefList<CborBytes>([.. transaction.TransactionWitnessSet.PlutusV3ScriptSet()!.Select(e => new CborBytes(e))]) : null
        );

        return new PostMaryTransaction(transaction.TransactionBody, newWitnessSet, transaction.IsValid, transaction.AuxiliaryData);
    }
}