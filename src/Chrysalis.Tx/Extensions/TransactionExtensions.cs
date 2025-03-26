using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Keys;

namespace Chrysalis.Tx.Extensions;
public static class TransactionExtension
{
    public static PostMaryTransaction Sign(this PostMaryTransaction self, PrivateKey privateKey)
    {
        var txBodyBytes = CborSerializer.Serialize(self.TransactionBody);
        var signature = privateKey.Sign(HashUtil.Blake2b256(txBodyBytes));
        var vkeyWitness = new VKeyWitness(privateKey.GetPublicKey().Key, signature);
        var vKeyWitnesses = self.TransactionWitnessSet.VkeyWitnessSet()?.ToList() ?? [];
        vKeyWitnesses.Add(vkeyWitness);

        return self with
        {
            TransactionWitnessSet = self.TransactionWitnessSet switch
            {
                AlonzoTransactionWitnessSet alonzoTransactionWitnessSet => alonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefList<VKeyWitness>(vKeyWitnesses)
                },
                PostAlonzoTransactionWitnessSet postAlonzoTransactionWitnessSet => postAlonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefList<VKeyWitness>(vKeyWitnesses)
                },
                _ => throw new Exception("Unknown transaction witness set type")
            }
        };
    }

    public static PostMaryTransaction Sign(this PostMaryTransaction self, List<VKeyWitness> vKeyWitnesses)
    {
        var vkeyWitnessSet = self.TransactionWitnessSet.VkeyWitnessSet()?.ToList() ?? [];
        vkeyWitnessSet.AddRange(vKeyWitnesses);

        return self with
        {
            TransactionWitnessSet = self.TransactionWitnessSet switch
            {
                AlonzoTransactionWitnessSet alonzoTransactionWitnessSet => alonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefList<VKeyWitness>(vkeyWitnessSet)
                },
                PostAlonzoTransactionWitnessSet postAlonzoTransactionWitnessSet => postAlonzoTransactionWitnessSet with
                {
                    VKeyWitnessSet = new CborDefList<VKeyWitness>(vkeyWitnessSet)
                },
                _ => throw new Exception("Unknown transaction witness set type")
            }
        };
    }
}