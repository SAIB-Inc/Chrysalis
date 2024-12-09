using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Header;
using Chrysalis.Cardano.Core.Types.Block.Header.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types.Collections;

namespace Chrysalis.Cardano.Core.Extensions;

public static class BlockExtension
{
    public static Block GetBlock(this Block block)
    => block switch
    {
        PostAlonzoBlock postAlonzoBlock => postAlonzoBlock,
        ShelleyBlock shelleyBlock => shelleyBlock,
        PreAlonzoBlock preAlonzoBlock => preAlonzoBlock,
        _ => throw new NotImplementedException()
    };

    public static string Hash(this Block block)
        => Convert.ToHexString(CborSerializer.Serialize(block.GetBlock().Header()).ToBlake2b()).ToLowerInvariant();

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
        PostAlonzoBlock postAlonzoBlock => postAlonzoBlock.Header,
        ShelleyBlock shelleyBlock => shelleyBlock.Header,
        PreAlonzoBlock preAlonzoBlock => preAlonzoBlock.Header,
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
        => block switch {
            PostAlonzoBlock postAlonzoBlock => postAlonzoBlock.TransactionBodies switch {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefiniteListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => throw new NotImplementedException()
            },
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionBodies switch {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefiniteListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => throw new NotImplementedException()
            },
            PreAlonzoBlock preAlonzoBlock => preAlonzoBlock.TransactionBodies switch {
                CborDefList<TransactionBody> x => x.Value,
                CborIndefList<TransactionBody> x => x.Value,
                CborDefiniteListWithTag<TransactionBody> x => x.Value,
                CborIndefListWithTag<TransactionBody> x => x.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this Block block)
        => block switch {
            PostAlonzoBlock postAlonzoBlock => postAlonzoBlock.TransactionWitnessSets switch {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefiniteListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => throw new NotImplementedException()
            },
            ShelleyBlock shelleyBlock => shelleyBlock.TransactionWitnessSets switch {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefiniteListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => throw new NotImplementedException()
            },
            PreAlonzoBlock preAlonzoBlock => preAlonzoBlock.TransactionWitnessSets switch {
                CborDefList<TransactionWitnessSet> x => x.Value,
                CborIndefList<TransactionWitnessSet> x => x.Value,
                CborDefiniteListWithTag<TransactionWitnessSet> x => x.Value,
                CborIndefListWithTag<TransactionWitnessSet> x => x.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this Block block)
        => block switch {
            PostAlonzoBlock postAlonzoBlock => postAlonzoBlock.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            PreAlonzoBlock preAlonzoBlock => preAlonzoBlock.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => throw new NotImplementedException()
        };
}
