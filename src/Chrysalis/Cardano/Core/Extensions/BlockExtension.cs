using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Header;
using Chrysalis.Cardano.Core.Types.Block.Header.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Collections;

namespace Chrysalis.Cardano.Core.Extensions;

public static class BlockExtension
{
    public static Block GetBlock(this Block block)
    => block switch
    {
        ShelleyBlock shelleyBlock => shelleyBlock,
        AlonzoBlock alonzoBlock => alonzoBlock,
        _ => throw new NotImplementedException()
    };

    public static string Hash(this Block block)
        => Convert.ToHexString(block.Header().Raw?.ToBlake2b256() ?? []).ToLowerInvariant();

    public static ulong Slot(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot.Value,
            _ => throw new NotImplementedException()
        };

    public static BlockHeader Header(this Block block)
    => block switch
    {
        ShelleyBlock shelleyBlock => shelleyBlock.Header,
        AlonzoBlock alonzoBlock => alonzoBlock.Header,
        _ => throw new NotImplementedException()
    };

    public static BlockHeaderBody HeaderBody(this Block block)
        => block.Header().HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody,
            _ => throw new NotImplementedException()
        };

    public static ulong Number(this Block block)
        => block.HeaderBody() switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber.Value,
            _ => throw new NotImplementedException()
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
                _ => throw new NotImplementedException()
            },
            AlonzoBlock alonzoBlock => alonzoBlock.TransactionBodies switch
            {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
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
                _ => throw new NotImplementedException()
            },
            AlonzoBlock alonzoBlock => alonzoBlock.TransactionWitnessSets switch
            {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this Block block)
    {
        return block switch
        {
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionMetadataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            AlonzoBlock alonzoBlock => alonzoBlock.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => throw new NotImplementedException()
        };
    }
}
