using TokenBundleMint = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TokenBundle.TokenBundleMint;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Mint;

public static class TokenBundleMintExtensions
{
    public static Dictionary<byte[], long> Value(this TokenBundleMint self) => self.Value;
}