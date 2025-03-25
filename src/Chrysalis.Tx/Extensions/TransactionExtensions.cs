using System.Reflection.Metadata.Ecma335;
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
    // public static PostMaryTransaction Sign(this PostMaryTransaction self, PrivateKey privateKey)
    // {
    //     var txBodyBytes = CborSerializer.Serialize(self.TransactionBody);
    //     var signature = privateKey.Sign(HashUtil.Blake2b256(txBodyBytes));
    //     var vkeyWitness = new VKeyWitness(new CborBytes(privateKey.GetPublicKey().Key), new CborBytes(signature));
    //     var vKeyWitnesses = self.TransactionWitnessSet.VKeyWitnessSet()?.ToList() ?? [];
    //     vKeyWitnesses.Add(vkeyWitness);
    //     // @TODO
    //     return self;
    // }

    // public static PostMaryTransaction Sign(this PostMaryTransaction self, List<VKeyWitness> vKeyWitnesses)
    // {
    //     var vkeyWitnessSet = self.TransactionWitnessSet.VKeyWitnessSet()?.ToList() ?? [];
    //     vkeyWitnessSet.AddRange(vKeyWitnesses);
    //     // @TODO
    //     return self;
    // }
}