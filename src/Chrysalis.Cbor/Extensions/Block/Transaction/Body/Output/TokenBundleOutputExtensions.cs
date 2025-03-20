using TokenBundleOutput = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output.TokenBundle.TokenBundleOutput;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Output;

public static class TokenBundleOutputExtensions
{
    public static Dictionary<byte[], ulong> Value(this TokenBundleOutput self) => self.Value;
}