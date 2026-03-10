using Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Utils;
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
    public static ITransaction Sign(this ITransaction self, PrivateKey privateKey)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(privateKey);

        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new InvalidOperationException("Transaction type not supported")
        };
        byte[] txBodyBytes = CborSerializer.Serialize(tx.Body);
        byte[] signature = privateKey.Sign(HashUtil.Blake2b256(txBodyBytes));
        VKeyWitness vkeyWitness = CborFactory.CreateVKeyWitness(privateKey.GetPublicKey().Key, signature);
        List<VKeyWitness> vKeyWitnesses = tx.Witnesses.VKeyWitnessSet() is not null ?
            [.. tx.Witnesses.VKeyWitnessSet()!] : [];

        vKeyWitnesses.Add(vkeyWitness);

        return RebuildWithWitnesses(tx, vKeyWitnesses);
    }

    /// <summary>
    /// Signs a transaction with the given list of VKey witnesses.
    /// </summary>
    public static ITransaction Sign(this ITransaction self, List<VKeyWitness> vKeyWitnesses)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(vKeyWitnesses);

        PostMaryTransaction tx = self switch
        {
            PostMaryTransaction postMaryTransaction => postMaryTransaction,
            _ => throw new InvalidOperationException("Transaction type not supported")
        };
        List<VKeyWitness> vkeyWitnessSet = tx.Witnesses.VKeyWitnessSet() is not null ?
            [.. tx.Witnesses.VKeyWitnessSet()!] : [];
        vkeyWitnessSet.AddRange(vKeyWitnesses);

        return RebuildWithWitnesses(tx, vkeyWitnessSet);
    }

    private static PostMaryTransaction RebuildWithWitnesses(PostMaryTransaction tx, List<VKeyWitness> vKeyWitnesses)
    {
        PostAlonzoTransactionWitnessSet existingWitnesses = tx.Witnesses switch
        {
            PostAlonzoTransactionWitnessSet ws => ws,
            _ => default
        };

        PostAlonzoTransactionWitnessSet newWitnessSet = CborFactory.CreateWitnessSet(
            vKeyWitnesses: CborFactory.CreateDefListWithTag<VKeyWitness>(vKeyWitnesses),
            nativeScripts: existingWitnesses.NativeScripts,
            bootstrapWitnesses: existingWitnesses.BootstrapWitnesses,
            plutusV1Scripts: existingWitnesses.PlutusV1Scripts,
            plutusDataSet: existingWitnesses.PlutusDataSet,
            redeemers: existingWitnesses.Redeemers,
            plutusV2Scripts: existingWitnesses.PlutusV2Scripts,
            plutusV3Scripts: existingWitnesses.PlutusV3Scripts
        );

        return CborFactory.CreatePostMaryTransaction(tx.Body, newWitnessSet, tx.IsValid, tx.AuxiliaryData);
    }
}
