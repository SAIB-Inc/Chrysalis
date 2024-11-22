using Chrysalis.Cardano.Core;

namespace Chrysalis.Utils;

public static class ValueUtils
{
    public static LovelaceWithMultiAsset TransactionValueLovelace(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset,
            _ => throw new NotImplementedException()
        };
}