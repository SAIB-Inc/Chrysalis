using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;

namespace Chrysalis.Utils;
    
public static class BlockUtils
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
            _ => throw new NotImplementedException()
        };
}
