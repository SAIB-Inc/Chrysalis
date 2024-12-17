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
        ShelleyBlock shelleyBlock => shelleyBlock,
        AlonzoBlock alonzoBlock => alonzoBlock,
        _ => null
    };

    public static string Hash(this Block block)
        => Convert.ToHexString(block.Header().Raw?.ToBlake2b256() ?? []).ToLowerInvariant();

    public static ulong? Slot(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot.Value,
            ShelleyHeaderBody shelleyHeaderBody => shelleyHeaderBody.Slot.Value,
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
        ShelleyBlock shelleyBlock => shelleyBlock.Header,
        AlonzoBlock alonzoBlock => alonzoBlock.Header,
        _ => null
    };

    public static BlockHeaderBody? HeaderBody(this Block block)
        => block.Header()?.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody,
            ShelleyHeaderBody shelleyHeaderBody => shelleyHeaderBody,
            _ => null
        };

    public static ulong? Number(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber.Value,
            ShelleyHeaderBody shelleyHeaderBody => shelleyHeaderBody.BlockNumber.Value,
            _ => null
        };

    public static IEnumerable<TransactionBody> TransactionBodies(this Block block)
        => block switch
        {
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionBodies switch
            {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => []
            },
            AlonzoBlock alonzoBlock => alonzoBlock.TransactionBodies switch
            {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => []
            },
            _ => []
        };

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this Block block)
        => block switch
        {
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionWitnessSets switch
            {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => []
            },
            AlonzoBlock alonzoBlock => alonzoBlock.TransactionWitnessSets switch
            {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => []
            },
            _ => []
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this Block block)
    {
        return block switch
        {
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionMetadataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            AlonzoBlock alonzoBlock => alonzoBlock.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => []
        };
    }

    public static IEnumerable<int>? InvalidTransactions(this Block block)
        => block switch
        {
            ShelleyBlock shelleyBlock => null,
            AlonzoBlock alonzoBlock => alonzoBlock.InvalidTransactions switch
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
