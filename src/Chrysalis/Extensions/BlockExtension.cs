using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;

namespace Chrysalis.Extensions;

public static class BlockExtension
{
    public static string Hash(this Block block)
        => Convert.ToHexString(CborSerializer.Serialize(block.Header).ToBlake2b()).ToLowerInvariant();

    public static ulong Slot(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot.Value,
            _ => throw new NotImplementedException()
        };

    public static bool HasTransactions(this Block block)
        => TransactionBodies(block).Any();

    public static ulong TransactionCount(this Block block)
        => (ulong)TransactionBodies(block).Count();

    public static byte[] PrevHash(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.PrevHash.Value.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.PrevHash.Value.Value,
            _ => throw new NotImplementedException()
        };

    public static BlockHeaderBody HeaderBody(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody,
            _ => throw new NotImplementedException()
        };

    public static ulong Number(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber.Value,
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionBody> TransactionBodies(this Block block)
        => block.TransactionBodies switch
        {
            CborDefiniteList<TransactionBody> x => x.Value,
            CborIndefiniteList<TransactionBody> x => x.Value,
            CborDefiniteListWithTag<TransactionBody> x => x.Value.Value,
            CborIndefiniteListWithTag<TransactionBody> x => x.Value.Value,
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this Block block)
        => block.TransactionWitnessSets switch
        {
            CborDefiniteList<TransactionWitnessSet> x => x.Value,
            CborIndefiniteList<TransactionWitnessSet> x => x.Value,
            CborDefiniteListWithTag<TransactionWitnessSet> x => x.Value.Value,
            CborIndefiniteListWithTag<TransactionWitnessSet> x => x.Value.Value,
            _ => throw new NotImplementedException()
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this Block block)
        => block.AuxiliaryDataSet.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value);
}
