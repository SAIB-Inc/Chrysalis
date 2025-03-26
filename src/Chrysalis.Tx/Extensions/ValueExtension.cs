using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

namespace Chrysalis.Tx.Extensions;

public static class ValueExtension
{
    public static ulong Lovelace(this Value self)
    {
        return self switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
            _ => throw new Exception("Unknown value type")
        };
    }

    public static MultiAsset MultiAssetOutput(this Value self)
    {
        return self switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset,
            _ => throw new Exception("Unknown value type")
        };
    }
}
