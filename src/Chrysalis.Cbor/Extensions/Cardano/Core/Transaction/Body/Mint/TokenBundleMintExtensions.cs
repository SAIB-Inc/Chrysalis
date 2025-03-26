using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Mint;

public static class TokenBundleMintExtensions
{
    public static Dictionary<byte[], long> Value(this TokenBundleMint self) => self.Value;
}