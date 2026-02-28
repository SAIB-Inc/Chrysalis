using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for signing Cardano transactions.
/// </summary>
public static class TransactionExtension
{
    /// <summary>
    /// Signs a transaction with the given private key.
    /// </summary>
    /// <param name="self">The transaction to sign.</param>
    /// <param name="privateKey">The private key to sign with.</param>
    /// <returns>The transaction with the added VKey witness.</returns>
    public static Transaction Sign(this Transaction self, PrivateKey privateKey)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(privateKey);

        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new InvalidOperationException("Transaction type not supported")
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
                _ => throw new InvalidOperationException("Unknown transaction witness set type")
            }
        };
    }

    /// <summary>
    /// Signs a transaction with the given list of VKey witnesses.
    /// </summary>
    /// <param name="self">The transaction to sign.</param>
    /// <param name="vKeyWitnesses">The VKey witnesses to add.</param>
    /// <returns>The transaction with the added VKey witnesses.</returns>
    public static Transaction Sign(this Transaction self, List<VKeyWitness> vKeyWitnesses)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(vKeyWitnesses);

        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new InvalidOperationException("Transaction type not supported")
        };
        List<VKeyWitness> vkeyWitnessSet = tx.TransactionWitnessSet.VKeyWitnessSet() is not null ?
            [.. tx.TransactionWitnessSet.VKeyWitnessSet()!] : [];
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
                _ => throw new InvalidOperationException("Unknown transaction witness set type")
            },
            Raw = null

        };
    }
}
