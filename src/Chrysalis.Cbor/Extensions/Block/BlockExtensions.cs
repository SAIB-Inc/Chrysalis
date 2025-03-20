using CBlock = Chrysalis.Cbor.Cardano.Types.Block.Block;
using Chrysalis.Cbor.Cardano.Types.Block.Header;
using static Chrysalis.Cbor.Cardano.Types.Block.Block;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Types.Custom;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.TransactionBody;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.TransactionWitnessSet;

namespace Chrysalis.Cbor.Extensions.Block;

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

    public static IEnumerable<TransactionBody> TransactionBodies(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionBodies switch
            {
                CborMaybeIndefList<AlonzoTransactionBody>.CborDefList defList => defList.Value,
                CborMaybeIndefList<AlonzoTransactionBody>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<AlonzoTransactionBody>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<AlonzoTransactionBody>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            BabbageBlock babbageBlock => babbageBlock.TransactionBodies switch
            {
                CborMaybeIndefList<BabbageTransactionBody>.CborDefList defList => defList.Value,
                CborMaybeIndefList<BabbageTransactionBody>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<BabbageTransactionBody>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<BabbageTransactionBody>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            ConwayBlock conwayBlock => conwayBlock.TransactionBodies switch
            {
                CborMaybeIndefList<ConwayTransactionBody>.CborDefList defList => defList.Value,
                CborMaybeIndefList<ConwayTransactionBody>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<ConwayTransactionBody>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<ConwayTransactionBody>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            _ => []
        };

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionWitnessSets switch
            {
                CborMaybeIndefList<AlonzoTransactionWitnessSet>.CborDefList defList => defList.Value,
                CborMaybeIndefList<AlonzoTransactionWitnessSet>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<AlonzoTransactionWitnessSet>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<AlonzoTransactionWitnessSet>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            BabbageBlock babbageBlock => babbageBlock.TransactionWitnessSets switch
            {
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborDefList defList => defList.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => []
            },
            ConwayBlock conwayBlock => conwayBlock.TransactionWitnessSets switch
            {
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborDefList defList => defList.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<PostAlonzoTransactionWitnessSet>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
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
                CborMaybeIndefList<int>.CborDefList defList => defList.Value,
                CborMaybeIndefList<int>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<int>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<int>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            BabbageBlock babbageBlock => babbageBlock.InvalidTransactions switch
            {
                CborMaybeIndefList<int>.CborDefList defList => defList.Value,
                CborMaybeIndefList<int>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<int>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<int>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            ConwayBlock conwayBlock => conwayBlock.InvalidTransactions switch
            {
                CborMaybeIndefList<int>.CborDefList defList => defList.Value,
                CborMaybeIndefList<int>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<int>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<int>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
}