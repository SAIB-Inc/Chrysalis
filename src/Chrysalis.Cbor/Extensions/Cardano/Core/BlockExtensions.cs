using CBlock = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core;

public static class BlockExtensions
{
    public static BlockHeader Header(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.Header,
            BabbageBlock babbageBlock => babbageBlock.Header,
            ConwayBlock conwayBlock => conwayBlock.Header,
            _ => throw new NotSupportedException()
        };

    public static IEnumerable<TransactionBody> TransactionBodies(this CBlock self)
    {
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionBodies.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.TransactionBodies.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.TransactionBodies.GetValue(),
            _ => []
        };
    }

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionWitnessSets switch
            {
                CborDefList<AlonzoTransactionWitnessSet> defList => defList.Value,
                CborIndefList<AlonzoTransactionWitnessSet> indefList => indefList.Value,
                CborDefListWithTag<AlonzoTransactionWitnessSet> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<AlonzoTransactionWitnessSet> indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            BabbageBlock babbageBlock => babbageBlock.TransactionWitnessSets switch
            {
                CborDefList<PostAlonzoTransactionWitnessSet> defList => defList.Value,
                CborIndefList<PostAlonzoTransactionWitnessSet> indefList => indefList.Value,
                CborDefListWithTag<PostAlonzoTransactionWitnessSet> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<PostAlonzoTransactionWitnessSet> indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            ConwayBlock conwayBlock => conwayBlock.TransactionWitnessSets switch
            {
                CborDefList<PostAlonzoTransactionWitnessSet> defList => defList.Value,
                CborIndefList<PostAlonzoTransactionWitnessSet> indefList => indefList.Value,
                CborDefListWithTag<PostAlonzoTransactionWitnessSet> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<PostAlonzoTransactionWitnessSet> indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            _ => []
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.AuxiliaryDataSet.Value,
            BabbageBlock babbageBlock => babbageBlock.AuxiliaryDataSet.Value,
            ConwayBlock conwayBlock => conwayBlock.AuxiliaryDataSet.Value,
            _ => []
        };

    public static IEnumerable<int>? InvalidTransactions(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.InvalidTransactions switch
            {
                CborDefList<int> defList => defList.Value,
                CborIndefList<int> indefList => indefList.Value,
                CborDefListWithTag<int> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<int> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageBlock babbageBlock => babbageBlock.InvalidTransactions switch
            {
                CborDefList<int> defList => defList.Value,
                CborIndefList<int> indefList => indefList.Value,
                CborDefListWithTag<int> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<int> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayBlock conwayBlock => conwayBlock.InvalidTransactions switch
            {
                CborDefList<int> defList => defList.Value,
                CborIndefList<int> indefList => indefList.Value,
                CborDefListWithTag<int> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<int> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
}