using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Header;
using Chrysalis.Cardano.Core.Types.Block.Header.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Extensions;

public static class BlockExtension
{
    public static Block? GetBlock(this Block block)
    => block switch
    {
        AlonzoCompatibleBlock alonzoBlock => alonzoBlock,
        BabbageBlock babbageBlock => babbageBlock,
        ConwayBlock conwayBlock => conwayBlock,
        _ => null
    };

    public static string Hash(this Block block)
        => Convert.ToHexString(block.Header()?.Raw?.ToBlake2b256() ?? []).ToLowerInvariant();

    public static ulong? Slot(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot.Value,
            _ => null
        };

    public static List<string> InputOutRefs(this Block block)
        => block.TransactionBodies()
            .SelectMany(
                tx => tx.Inputs(),
                (tx, input) => input.TransactionId() + input.Index().ToString()
            )
            .ToList();

    public static BlockHeader? Header(this Block block)
    => block switch
    {
        AlonzoCompatibleBlock a => a.Header,
        BabbageBlock b => b.Header,
        ConwayBlock c => c.Header,
        _ => null
    };

    public static BlockHeaderBody? HeaderBody(this Block block)
        => block.Header()?.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody,
            _ => null
        };

    public static ulong? Number(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber.Value,
            _ => null
        };

    public static IEnumerable<TransactionBody> TransactionBodies(this Block block)
        => block switch
        {

            AlonzoCompatibleBlock a => a.TransactionBodies switch
            {
                CborDefList<AlonzoTransactionBody> x => x.Value,
                CborIndefList<AlonzoTransactionBody> x => x.Value,
                CborDefListWithTag<AlonzoTransactionBody> x => x.Value,
                CborIndefListWithTag<AlonzoTransactionBody> x => x.Value,
                _ => []
            },
            BabbageBlock b => b.TransactionBodies switch
            {
                CborDefList<BabbageTransactionBody> x => x.Value,
                CborIndefList<BabbageTransactionBody> x => x.Value,
                CborDefListWithTag<BabbageTransactionBody> x => x.Value,
                CborIndefListWithTag<BabbageTransactionBody> x => x.Value,
                _ => []
            },
            ConwayBlock c => c.TransactionBodies switch
            {
                CborDefList<ConwayTransactionBody> x => x.Value,
                CborIndefList<ConwayTransactionBody> x => x.Value,
                CborDefListWithTag<ConwayTransactionBody> x => x.Value,
                CborIndefListWithTag<ConwayTransactionBody> x => x.Value,
                _ => []
            },
            _ => []
        };

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this Block block)
        => block switch
        {
            AlonzoCompatibleBlock a => a.TransactionWitnessSets switch
            {
                CborDefList<AlonzoTransactionWitnessSet> x => x.Value,
                CborIndefList<AlonzoTransactionWitnessSet> x => x.Value,
                CborDefListWithTag<AlonzoTransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<AlonzoTransactionWitnessSet> x => x.Value,
                _ => []
            },
            BabbageBlock b => b.TransactionWitnessSets switch
            {
                CborDefList<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborIndefList<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborDefListWithTag<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<PostAlonzoTransactionWitnessSet> x => x.Value,
                _ => []
            },
            ConwayBlock c => c.TransactionWitnessSets switch
            {
                CborDefList<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborIndefList<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborDefListWithTag<PostAlonzoTransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<PostAlonzoTransactionWitnessSet> x => x.Value,
                _ => []
            },
            _ => []
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this Block block)
    {
        return block switch
        {
            AlonzoCompatibleBlock a => a.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            BabbageBlock b => b.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            ConwayBlock c => c.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => []
        };
    }

    public static IEnumerable<int>? InvalidTransactions(this Block block)
        => block switch
        {
            BabbageBlock b => b.InvalidTransactions switch
            {
                CborDefList<CborInt> x => x.Value.Select(x => x.Value),
                CborIndefList<CborInt> x => x.Value.Select(x => x.Value),
                CborDefListWithTag<CborInt> x => x.Value.Select(x => x.Value),
                CborIndefListWithTag<CborInt> x => x.Value.Select(x => x.Value),
                _ => []
            },
            ConwayBlock c => c.InvalidTransactions switch
            {
                CborDefList<CborInt> x => x.Value.Select(x => x.Value),
                CborIndefList<CborInt> x => x.Value.Select(x => x.Value),
                CborDefListWithTag<CborInt> x => x.Value.Select(x => x.Value),
                CborIndefListWithTag<CborInt> x => x.Value.Select(x => x.Value),
                _ => []
            },
            _ => []
        };
}
