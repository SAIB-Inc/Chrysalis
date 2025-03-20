using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using LovelaceWithMultiAsset = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.Value.LovelaceWithMultiAsset;
using LovelaceValue = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.Value.Lovelace;
using TokenBundleOutput = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TokenBundle.TokenBundleOutput;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Output;

public static class ValueExtensions
{
    public static ulong Lovelace(this Value self) =>
        self switch
        {
            LovelaceValue lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
            _ => default
        };

    public static Dictionary<byte[], TokenBundleOutput> MultiAsset(this Value self) =>
        self switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value,
            _ => []
        };
}