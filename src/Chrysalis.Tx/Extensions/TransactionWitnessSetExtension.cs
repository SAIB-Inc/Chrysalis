using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Tx.Extensions;

public static class TransactionWitnessSetExtension
{
    public static IEnumerable<VKeyWitness>? VkeyWitnessSet(this TransactionWitnessSet self)
    {
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTransactionWitnessSet => alonzoTransactionWitnessSet.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> cborDefList => cborDefList.Value.Select(x => x),
                CborIndefList<VKeyWitness> cborIndefList => cborIndefList.Value.Select(x => x),
                CborDefListWithTag<VKeyWitness> cborDefListWithTag => cborDefListWithTag.Value.Select(x => x),
                CborIndefListWithTag<VKeyWitness> cborIndefListWithTag => cborIndefListWithTag.Value.Select(x => x),
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTransactionWitnessSet => postAlonzoTransactionWitnessSet.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> cborDefList => cborDefList.Value.Select(x => x),
                CborIndefList<VKeyWitness> cborIndefList => cborIndefList.Value.Select(x => x),
                CborDefListWithTag<VKeyWitness> cborDefListWithTag => cborDefListWithTag.Value.Select(x => x),
                CborIndefListWithTag<VKeyWitness> cborIndefListWithTag => cborIndefListWithTag.Value.Select(x => x),
                _ => null
            },
            _ => null
        };
    }
}