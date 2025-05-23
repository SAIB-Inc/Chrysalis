using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Tx.Extensions;
public static class TransactionExtension
{
    public static Transaction Sign(this Transaction self, PrivateKey privateKey)
    {
        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new Exception("Transaction type not supported")
        };
        byte[] txBodyBytes = CborSerializer.Serialize(tx.TransactionBody);
        byte[] signature = privateKey.Sign(HashUtil.Blake2b256(txBodyBytes));
        VKeyWitness vkeyWitness = new(privateKey.GetPublicKey().Key, signature);
        List<VKeyWitness> vKeyWitnesses = tx.TransactionWitnessSet.VKeyWitnessSet() is not null ?
            [.. tx.TransactionWitnessSet.VKeyWitnessSet()!] : [];

        vKeyWitnesses.Add(vkeyWitness);

        return tx with
        {
            TransactionWitnessSet = tx.TransactionWitnessSet switch
            {
                AlonzoTransactionWitnessSet alonzoTransactionWitnessSet => alonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>(vKeyWitnesses)
                },
                PostAlonzoTransactionWitnessSet postAlonzoTransactionWitnessSet => postAlonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>(vKeyWitnesses)
                },
                _ => throw new Exception("Unknown transaction witness set type")
            }
        };
    }

    public static Transaction Sign(this Transaction self, List<VKeyWitness> vKeyWitnesses)
    {
        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new Exception("Transaction type not supported")
        };
        var vkeyWitnessSet = tx.TransactionWitnessSet.VKeyWitnessSet() is not null ?
            tx.TransactionWitnessSet.VKeyWitnessSet()!.ToList() : [];
        vkeyWitnessSet.AddRange(vKeyWitnesses);

        return tx with
        {
            TransactionWitnessSet = tx.TransactionWitnessSet switch
            {
                AlonzoTransactionWitnessSet alonzoTransactionWitnessSet => alonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>(vkeyWitnessSet),
                    Raw = null
                },
                PostAlonzoTransactionWitnessSet postAlonzoTransactionWitnessSet => postAlonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefListWithTag<VKeyWitness>(vkeyWitnessSet),
                    Raw = null
                },
                _ => throw new Exception("Unknown transaction witness set type")
            },
            Raw = null
            
        };
    }
}